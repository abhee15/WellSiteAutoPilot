using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class DirectAdministratorRecoveryChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile administrator, Guid assetId, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var adminRole = await db.RoleDefinitions.SingleAsync(x => x.Key == "admin" && x.IsSystem);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "direct-admin-recovery");
        var users = new UserAccessRepository(db);
        var targets = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var identity = $"RECOVERY\\{Guid.NewGuid():N}";
            targets.Add((await users.UpsertAuthenticatedUserAsync(identity, identity,
                identity.ToUpperInvariant(), null, clock.GetUtcNow())).Id);
        }
        var first = Grant(targets[0]);
        var inactiveUser = Grant(targets[1]);
        var retiredGrant = Grant(targets[2]);
        retiredGrant.IsActive = false;
        var allAssets = Grant(targets[3], "AllAssets");
        var malformedScope = Grant(targets[4]);
        var group = new AccessGrantEntity { Id = Guid.NewGuid(), SubjectKind = "WindowsGroup",
            GroupSid = "S-1-5-21-123-234-345-456", RoleId = adminRole.Id, ScopeKind = "DeploymentWide",
            IsActive = true, Revision = 1, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        var customRole = await new RoleCatalogService(new RoleCatalogRepository(db), users, clock)
            .CreateAsync(new RoleMutationCommand("Recovery custom " + Guid.NewGuid().ToString("N"), "",
                [WellSitePermissions.SecurityManage], true), actor);
        var custom = new AccessGrantEntity { Id = Guid.NewGuid(), SubjectKind = "User", UserId = targets[5],
            RoleId = customRole.Id, ScopeKind = "DeploymentWide", IsActive = true, Revision = 1,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        var fixtures = new[] { first, inactiveUser, retiredGrant, allAssets, malformedScope, group, custom };
        db.AccessGrants.AddRange(fixtures);
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = malformedScope.Id, AssetId = assetId });
        await db.SaveChangesAsync();
        await db.Users.Where(x => x.Id == targets[1]).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        // Neither legacy administrators nor group/custom/scoped/inactive/malformed
        // records substitute for a directly assigned recovery administrator.
        var beforeAudits = await db.AuditEvents.CountAsync();
        await ExpectAsync(() => RetireAsync(first), "SECURITY_LAST_DIRECT_ADMIN_REQUIRED");
        await ExpectAsync(() => RetireAsync(first, revision: 2), "SECURITY_GRANT_CONFLICT");
        await ExpectAsync(() => RetireAsync(first, actor with { UserId = targets[5] }), "SECURITY_ADMIN_REQUIRED");
        await AssertActiveAsync(first.Id);
        if (await db.AuditEvents.CountAsync() != beforeAudits)
            throw new InvalidOperationException("Denied recovery changes wrote an audit or assignment.");

        // A valid second direct administrator permits retirement, but audit
        // failure must roll back the grant revision and status.
        var second = Grant(targets[2]);
        db.AccessGrants.Add(second);
        await db.SaveChangesAsync();
        try
        {
            await RetireAsync(first, actor with { IdentityName = new string('x', 257) });
            throw new InvalidOperationException("Invalid recovery audit committed.");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "22001" }) { }
        await AssertActiveAsync(first.Id);
        if (await db.AuditEvents.CountAsync() != beforeAudits)
            throw new InvalidOperationException("Failed recovery audit left partial writes.");

        // Force both serializable transactions to observe the other direct
        // administrator before either writes: this is write skew across two
        // distinct grants, not merely a same-row optimistic revision test.
        var barrier = new RecoveryReadBarrier();
        var racingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
            .AddInterceptors(barrier).Options;
        var outcomes = await Task.WhenAll(RaceAsync(first), RaceAsync(second));
        if (outcomes.Count(x => x) != 1)
            throw new InvalidOperationException("Concurrent recovery retirement did not preserve exactly one administrator.");
        var ids = new[] { first.Id, second.Id };
        var survivor = await db.AccessGrants.AsNoTracking().SingleAsync(x => ids.Contains(x.Id) && x.IsActive);
        if (await db.AuditEvents.CountAsync(x => x.Action == "security.grant.deactivated" &&
                (x.TargetId == first.Id.ToString() || x.TargetId == second.Id.ToString())) != 1)
            throw new InvalidOperationException("Concurrent recovery retirement did not have one atomic audit.");
        await ExpectAsync(() => RetireAsync(survivor), "SECURITY_LAST_DIRECT_ADMIN_REQUIRED");
        await AssertActiveAsync(survivor.Id);

        // Scoped/group/custom grants may still be retired while the sole
        // direct administrator is preserved; the guard must not freeze all edits.
        foreach (var unrelated in new[] { allAssets, group, custom }) await RetireAsync(unrelated);
        await AssertActiveAsync(survivor.Id);

        // These are isolated test fixtures, removed before the next suite.
        var fixtureIds = fixtures.Select(x => x.Id).Append(second.Id).ToArray();
        await db.AccessGrantAssets.Where(x => fixtureIds.Contains(x.GrantId)).ExecuteDeleteAsync();
        await db.AccessGrants.Where(x => fixtureIds.Contains(x.Id)).ExecuteDeleteAsync();
        Console.WriteLine("Direct recovery administrator scope, active-user, audit rollback and deterministic write-skew protection passed.");

        AccessGrantEntity Grant(Guid userId, string scope = "DeploymentWide") => new()
        {
            Id = Guid.NewGuid(), SubjectKind = "User", UserId = userId, RoleId = adminRole.Id,
            ScopeKind = scope, IsActive = true, Revision = 1,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
        };
        async Task RetireAsync(AccessGrantEntity grant, SecurityActorContext? securityActor = null, long revision = 1)
        {
            await using var writer = new WellSiteAutoPilotDbContext(options);
            await new AccessGrantRepository(writer).DeactivateAsync(
                grant.SubjectKind == "User" ? AccessGrantSubject.ForUser(grant.UserId!.Value)
                    : AccessGrantSubject.ForWindowsGroup(grant.GroupSid),
                grant.Id, revision, securityActor ?? actor, clock.GetUtcNow());
        }
        async Task<bool> RaceAsync(AccessGrantEntity grant)
        {
            await using var writer = new WellSiteAutoPilotDbContext(racingOptions);
            try
            {
                await new AccessGrantRepository(writer).DeactivateForUserAsync(
                    grant.UserId!.Value, grant.Id, 1, actor, clock.GetUtcNow());
                return true;
            }
            catch (WellSiteAutoPilotException e) when (e.Code == "SECURITY_GRANT_CONFLICT") { return false; }
        }
        async Task AssertActiveAsync(Guid grantId)
        {
            if (!await db.AccessGrants.AsNoTracking().AnyAsync(x => x.Id == grantId && x.IsActive && x.Revision == 1))
                throw new InvalidOperationException("Recovery administrator was partially retired.");
        }
    }

    private static async Task ExpectAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException e) when (e.Code == code) { return; }
        throw new InvalidOperationException($"Expected recovery refusal {code}.");
    }

    private sealed class RecoveryReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource bothReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readers;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("Remaining direct recovery administrators", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref readers) == 2) bothReaders.TrySetResult();
                await bothReaders.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
