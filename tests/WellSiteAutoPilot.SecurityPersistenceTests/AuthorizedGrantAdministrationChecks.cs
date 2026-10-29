using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedGrantAdministrationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid directAdministrator,
        AccessSubject groupAdministrator, Guid recoveryGrant)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var direct = new AccessSubject(directAdministrator);
        var directActor = new SecurityActorContext(directAdministrator, "direct-grant-admin", "grant-assignment-probe");
        var groupActor = new SecurityActorContext(groupAdministrator.UserId, "group-grant-admin", "grant-assignment-probe");
        await using var roleContext = new WellSiteAutoPilotDbContext(options);
        var role = await new RoleCatalogService(new RoleCatalogRepository(roleContext), new UserAccessRepository(roleContext), TimeProvider.System)
            .CreateAuthorizedAsync(new("Grant target " + Guid.NewGuid().ToString("N"), "Assignment target", [WellSitePermissions.AssetsRead]), direct, directActor);
        var typeId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        setup.AssetTypes.Add(new AssetTypeEntity
        {
            Id = typeId, Key = "grant-probe-" + typeId.ToString("N"), DisplayName = "Grant probe",
            AttributeSchemaJson = "{\"type\":\"object\"}", SchemaVersion = 1, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        setup.Assets.Add(new AssetEntity
        {
            Id = assetId, AssetTypeId = typeId, Name = "Grant probe", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await setup.SaveChangesAsync();
        var command = new CreateAccessGrantCommand(role.Id, AccessGrantScopeKind.AllAssets, [], role.Revision);
        var targetOther = AccessGrantSubject.ForUser(groupAdministrator.UserId);
        var targetDirect = AccessGrantSubject.ForUser(directAdministrator);
        var ownGroup = AccessGrantSubject.ForWindowsGroup(groupAdministrator.Groups!.GroupIds.Single());
        var externalGroup = AccessGrantSubject.ForWindowsGroup("S-1-5-21-915-991");
        await ExpectAsync(() => CreateAsync(targetDirect, command, direct, directActor), FailureKind.Authorization);
        await ExpectAsync(() => CreateAsync(ownGroup, command, groupAdministrator, groupActor), FailureKind.Authorization);
        await ExpectAsync(() => CreateAsync(targetDirect, command, new(groupAdministrator.UserId), groupActor), FailureKind.Authorization);
        await ExpectAsync(() => CreateAsync(targetOther, command with { ExpectedRoleRevision = 2 }, direct, directActor), FailureKind.Conflict);
        await ExpectAsync(() => CreateAsync(targetOther, command with { ScopeKind = AccessGrantScopeKind.ExactAssets }, direct, directActor), FailureKind.Validation);
        // A group administrator is not a substitute for the last direct
        // recovery administrator, even though its current authority is valid.
        await ExpectAsync(() => RetireAsync(targetDirect, recoveryGrant, 1, groupAdministrator, groupActor), FailureKind.Conflict);
        if (!await setup.AccessGrants.AnyAsync(item => item.Id == recoveryGrant && item.IsActive))
            throw new InvalidOperationException("Protected grant administration removed the last direct administrator.");

        var first = await CreateAsync(targetOther, command, direct, directActor);
        await ExpectAsync(() => CreateAsync(targetOther, command, direct, directActor), FailureKind.Conflict);
        await ExpectAsync(() => RetireAsync(targetOther, first.Id, 1, groupAdministrator, groupActor), FailureKind.Authorization);
        await ExpectAsync(() => RetireAsync(targetOther, first.Id, 2, direct, directActor), FailureKind.Conflict);
        await RetireAsync(targetOther, first.Id, 1, direct, directActor);
        var second = await CreateAsync(targetDirect, command, groupAdministrator, groupActor);
        await RetireAsync(targetDirect, second.Id, 1, groupAdministrator, groupActor);
        var group = await CreateAsync(externalGroup, command, groupAdministrator, groupActor);
        await RetireAsync(externalGroup, group.Id, 1, groupAdministrator, groupActor);
        var baseline = await setup.AuditEvents.CountAsync(item => item.CorrelationId == directActor.CorrelationId);
        if (baseline != 7 || await setup.UserRoles.AnyAsync()) throw new InvalidOperationException("Grant authoring used legacy roles or lost atomic audit.");
        var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        try
        {
            await CreateAsync(targetOther, command, direct, directActor, failingOptions);
            throw new InvalidOperationException("Assignment failure injection did not run.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "assignment-save-probe") { }
        if (await setup.AccessGrants.AnyAsync(item => item.RoleId == role.Id && item.IsActive) ||
            await setup.AuditEvents.CountAsync(item => item.CorrelationId == directActor.CorrelationId) != baseline)
            throw new InvalidOperationException("Grant creation failure left active scope or audit.");

        // Target-role retirement must wait for the new assignment/audit commit;
        // retaining only the acting administrator's grant would not prove this.
        var pause = new PauseAfterSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        var write = CreateAsync(targetOther, command with { ScopeKind = AccessGrantScopeKind.ExactAssets, AssetIds = [assetId] }, direct, directActor, pausedOptions);
        AccessGrant concurrent;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "assignment-role-retire-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var retire = new NpgsqlConnection(connection.ConnectionString);
            await retire.OpenAsync();
            await using var retireCommand = new NpgsqlCommand("UPDATE security.role_definitions SET \"IsActive\" = FALSE WHERE \"Id\" = $1", retire);
            retireCommand.Parameters.AddWithValue(role.Id);
            var retirement = retireCommand.ExecuteNonQueryAsync();
            try
            {
                await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
                await observer.OpenAsync();
                await using var waiting = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = $1
                    AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0)
                    """, observer);
                waiting.Parameters.AddWithValue(connection.ApplicationName!);
                var timer = Stopwatch.StartNew();
                while (!Equals(await waiting.ExecuteScalarAsync(), true))
                {
                    if (retirement.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                        throw new InvalidOperationException("Assignment creation did not retain its reviewed role through commit.");
                    await Task.Delay(20);
                }
            }
            finally { pause.Release.TrySetResult(); }
            concurrent = await write;
            if (await retirement != 1) throw new InvalidOperationException("Target role retirement did not complete.");
        }
        finally { pause.Release.TrySetResult(); await write; }
        await ExpectAsync(() => CreateAsync(targetDirect, command, groupAdministrator, groupActor), FailureKind.Authorization);
        if (concurrent.AssetIds.Count != 1 || !concurrent.AssetIds.Contains(assetId) ||
            await setup.AccessGrantAssets.CountAsync(item => item.GrantId == concurrent.Id && item.AssetId == assetId) != 1 ||
            await ScopedAccessQueries.ResourceGrants(setup, groupAdministrator.UserId, WellSitePermissions.AssetsRead)
                .AnyAsync(item => item.Id == concurrent.Id) ||
            await setup.AuditEvents.CountAsync(item => item.CorrelationId == directActor.CorrelationId) != baseline + 1)
            throw new InvalidOperationException("Retired target role retained authority or a denied assignment changed audit.");
        await setup.AccessGrants.Where(item => item.RoleId == role.Id).ExecuteDeleteAsync();
        await setup.RolePermissions.Where(item => item.RoleId == role.Id).ExecuteDeleteAsync();
        await setup.RoleDefinitions.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
        await setup.Assets.Where(item => item.Id == assetId).ExecuteDeleteAsync();
        await setup.AssetTypes.Where(item => item.Id == typeId).ExecuteDeleteAsync();
        await setup.AuditEvents.Where(item => item.CorrelationId == directActor.CorrelationId).ExecuteDeleteAsync();
        Console.WriteLine("Grant-based assignment writes preserve direct/group administration, own-access and last-direct recovery safeguards, atomic audit and held target-role authority.");

        async Task<AccessGrant> CreateAsync(AccessGrantSubject target, CreateAccessGrantCommand mutation,
            AccessSubject administrator, SecurityActorContext actor, DbContextOptions<WellSiteAutoPilotDbContext>? writeOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(writeOptions ?? options);
            return await new AccessGrantAdministrationService(new AccessGrantRepository(db), new UserAccessRepository(db), new RoleCatalogRepository(db), TimeProvider.System)
                .CreateAuthorizedAsync(target, mutation, administrator, actor);
        }
        async Task<AccessGrant> RetireAsync(AccessGrantSubject target, Guid grantId, long revision, AccessSubject administrator, SecurityActorContext actor)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new AccessGrantAdministrationService(new AccessGrantRepository(db), new UserAccessRepository(db), new RoleCatalogRepository(db), TimeProvider.System)
                .DeactivateAuthorizedAsync(target, grantId, revision, administrator, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected assignment refusal: {kind}.");
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("assignment-save-probe");
    }
    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
