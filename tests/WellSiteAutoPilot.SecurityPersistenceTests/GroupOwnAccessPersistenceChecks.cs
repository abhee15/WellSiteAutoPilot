using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class GroupOwnAccessPersistenceChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessGrantSubject group, AccessGrant grant, CreateAccessGrantCommand command,
        SecurityActorContext actor, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var now = clock.GetUtcNow();
        var own = actor with
        {
            RequiresGroupProof = true,
            Groups = new VerifiedWindowsGroupMembership(actor.UserId!.Value,
                ["S-1-5-21-110-220-330-999", group.GroupId!], now)
        };
        var beforeGrants = await db.AccessGrants.CountAsync();
        var beforeAudits = await db.AuditEvents.CountAsync();
        foreach (var denied in new[]
        {
            own,
            own with { Groups = null },
            own with { Groups = new VerifiedWindowsGroupMembership(actor.UserId.Value, [], now.AddMinutes(-2)) },
            own with { Groups = new VerifiedWindowsGroupMembership(Guid.NewGuid(), [], now) }
        })
        {
            var code = ReferenceEquals(denied, own) ? "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN" : "SECURITY_GROUP_MEMBERSHIP_REQUIRED";
            await CheckAsync(async (repository, service, roles) =>
                { await service.CreateAsync(group, command, denied); }, code);
            await CheckAsync(async (repository, service, roles) =>
                { await repository.CreateAsync(group, command, denied, now); }, code);
            await CheckAsync(async (repository, service, roles) =>
                { await service.DeactivateAsync(group, grant.Id, grant.Revision, denied); }, code);
            await CheckAsync(async (repository, service, roles) =>
                { await repository.DeactivateAsync(group, grant.Id, grant.Revision, denied, now); }, code);
            await CheckAsync(async (repository, service, roles) =>
            {
                var current = await roles.GetAsync(command.RoleId) ?? throw new InvalidOperationException("Missing role.");
                await roles.UpdateAsync(current.Id, current.Revision,
                    new RoleMutationCommand(current.Name, current.Description, current.Permissions, false), denied, now);
            }, code);
        }
        if (await db.AccessGrants.CountAsync() != beforeGrants || await db.AuditEvents.CountAsync() != beforeAudits ||
            !await db.AccessGrants.AnyAsync(x => x.Id == grant.Id && x.IsActive && x.Revision == grant.Revision) ||
            !await db.RoleDefinitions.AnyAsync(x => x.Id == command.RoleId && x.IsActive && x.Revision == command.ExpectedRoleRevision))
            throw new InvalidOperationException("Denied own-group mutation changed state or audit.");

        // Valid proof may change another group, and must not cross-union the
        // membership set with the target assignment subject.
        var other = AccessGrantSubject.ForWindowsGroup("S-1-5-21-110-220-330-998");
        await using var allowed = new WellSiteAutoPilotDbContext(options);
        var allowedRepository = new AccessGrantRepository(allowed);
        var saved = await allowedRepository.CreateAsync(other, command, own, now);
        await allowedRepository.DeactivateAsync(other, saved.Id, saved.Revision, own, now);

        // Seed an already assigned direct role as an isolated fixture. The
        // shared test deployment deliberately has only one legacy Admin, so
        // do not elevate a second caller merely to set up this refusal.
        var directId = Guid.NewGuid();
        db.AccessGrants.Add(new AccessGrantEntity
        {
            Id = directId, SubjectKind = "User", UserId = actor.UserId.Value,
            RoleId = command.RoleId, ScopeKind = command.ScopeKind.ToString(), IsActive = true,
            Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        db.AccessGrantAssets.AddRange(command.AssetIds.Select(assetId => new AccessGrantAssetEntity
        { GrantId = directId, AssetId = assetId }));
        await db.SaveChangesAsync();
        var directActor = own with { Groups = new VerifiedWindowsGroupMembership(actor.UserId.Value, [], now) };
        await CheckAsync(async (repository, service, roles) =>
        {
            var role = await roles.GetAsync(command.RoleId) ?? throw new InvalidOperationException("Missing role.");
            await roles.UpdateAsync(role.Id, role.Revision,
                new RoleMutationCommand(role.Name, role.Description, role.Permissions, false), directActor, now);
        }, "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN");
        await db.AccessGrantAssets.Where(x => x.GrantId == directId).ExecuteDeleteAsync();
        await db.AccessGrants.Where(x => x.Id == directId).ExecuteDeleteAsync();
        Console.WriteLine("Group-own assignment/service/transaction and role-deactivation protections reject stale/missing/wrong-user proof atomically.");


        async Task CheckAsync(Func<AccessGrantRepository, AccessGrantAdministrationService, RoleCatalogRepository, Task> operation, string code)
        {
            await using var attempt = new WellSiteAutoPilotDbContext(options);
            var repository = new AccessGrantRepository(attempt);
            var roles = new RoleCatalogRepository(attempt);
            var service = new AccessGrantAdministrationService(repository, new UserAccessRepository(attempt), roles, clock);
            try { await operation(repository, service, roles); }
            catch (WellSiteAutoPilotException e) when (e.Code == code) { return; }
            throw new InvalidOperationException($"Expected own-group persistence refusal {code}.");
        }
    }
}
