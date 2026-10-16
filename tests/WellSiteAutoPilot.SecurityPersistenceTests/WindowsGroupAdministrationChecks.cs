using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class WindowsGroupAdministrationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile administrator, UserAccessProfile ordinaryUser, Guid assetId, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "group-admin-test")
        { RequiresWindowsGroupProof = false }; // Explicit CI authoring without group runtime authority.
        var users = new UserAccessRepository(db);
        var roles = new RoleCatalogRepository(db);
        var catalog = new RoleCatalogService(roles, users, clock);
        var role = await catalog.CreateAsync(new RoleMutationCommand("Group custom role", "Group checks",
            [WellSitePermissions.AssetsRead, WellSitePermissions.LogicRead], true), actor);
        var repository = new AccessGrantRepository(db);
        var service = new AccessGrantAdministrationService(repository, users, roles, clock);
        var group = AccessGrantSubject.ForWindowsGroup("s-1-5-21-110-220-330-440");
        var otherGroup = AccessGrantSubject.ForWindowsGroup("S-1-5-21-110-220-330-441");
        var command = new CreateAccessGrantCommand(role.Id, AccessGrantScopeKind.ExactAssets, [assetId], role.Revision);
        if (group.GroupSid != "S-1-5-21-110-220-330-440" || group.UserId is not null)
            throw new InvalidOperationException("Group assignment target was not canonicalized.");
        foreach (var invalid in new[] { "", "FIELD\\Operators", "S-1-5-01", "S-1-5-4294967296", " S-1-5-21" })
        {
            try { _ = AccessGrantSubject.ForWindowsGroup(invalid); throw new InvalidOperationException("Invalid group accepted."); }
            catch (WellSiteAutoPilotException e) when (e.Code == "SECURITY_GRANT_SUBJECT_INVALID") { }
        }
        var ordinaryActor = new SecurityActorContext(ordinaryUser.Id, ordinaryUser.IdentityName, "group-denied");
        await ExpectAsync(() => service.CreateAsync(group, command, ordinaryActor), FailureKind.Authorization);
        await ExpectAsync(() => service.ListAsync(group, ordinaryActor), FailureKind.Authorization);
        await ExpectAsync(() => service.CreateAsync(group, command with { ExpectedRoleRevision = role.Revision + 1 }, actor), FailureKind.Conflict);
        await ExpectAsync(() => service.CreateAsync(group, command with { AssetIds = [] }, actor), FailureKind.Validation);

        // A failed audit must leave neither assignment nor Asset binding.
        var beforeGrants = await db.AccessGrants.CountAsync();
        var beforeBindings = await db.AccessGrantAssets.CountAsync();
        var beforeAudits = await db.AuditEvents.CountAsync();
        await using (var failing = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await new AccessGrantRepository(failing).CreateAsync(group, command,
                    actor with { IdentityName = new string('x', 257) }, clock.GetUtcNow());
                throw new InvalidOperationException("Invalid audit accepted.");
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "22001" }) { }
        }
        if (beforeGrants != await db.AccessGrants.CountAsync() || beforeBindings != await db.AccessGrantAssets.CountAsync() ||
            beforeAudits != await db.AuditEvents.CountAsync()) throw new InvalidOperationException("Partial group creation survived failed audit.");

        await GrantConcurrencyFailureChecks.RunAsync(options, group, command, actor, clock.GetUtcNow());

        var grant = await service.CreateAsync(group, command, actor);
        if (grant.SubjectKind != AccessGrantSubjectKind.WindowsGroup || grant.UserId is not null ||
            grant.GroupSid != group.GroupSid || grant.AssetIds.Single() != assetId)
            throw new InvalidOperationException("Group assignment did not round-trip.");
        await using (var reloaded = new WellSiteAutoPilotDbContext(options))
        {
            var saved = (await new AccessGrantRepository(reloaded).ListAsync(group)).Single();
            if (saved.Id != grant.Id || saved.GroupSid != group.GroupSid || !saved.IsActive)
                throw new InvalidOperationException("Group assignment was not durable.");
        }
        await GroupOwnAccessPersistenceChecks.RunAsync(options, group, grant, command, actor, clock);
        if ((await service.ListAsync(otherGroup, actor)).Count != 0)
            throw new InvalidOperationException("Group inspection crossed subject boundary.");
        await ExpectAsync(() => service.DeactivateAsync(otherGroup, grant.Id, 1, actor), FailureKind.NotFound);
        await ExpectAsync(() => service.DeactivateForUserAsync(ordinaryUser.Id, grant.Id, 1, actor), FailureKind.NotFound);
        await ExpectAsync(() => service.CreateAsync(group, command, actor), FailureKind.Conflict);
        await ExpectAsync(() => catalog.UpdateAsync(role.Id, role.Revision,
            new RoleMutationCommand(role.Name, role.Description, [WellSitePermissions.AssetsManage], true), actor), FailureKind.Conflict);

        // The same group can hold independent resource and deployment assignments.
        var global = await service.CreateAsync(group, command with { ScopeKind = AccessGrantScopeKind.DeploymentWide, AssetIds = [] }, actor);
        if ((await service.ListAsync(group, actor)).Count != 2 || global.AssetIds.Count != 0)
            throw new InvalidOperationException("Independent group scopes were collapsed.");
        await service.DeactivateAsync(group, global.Id, 1, actor);

        await using (var failing = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await new AccessGrantRepository(failing).DeactivateAsync(group, grant.Id, 1,
                    actor with { IdentityName = new string('x', 257) }, clock.GetUtcNow());
                throw new InvalidOperationException("Invalid retirement audit accepted.");
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "22001" }) { }
        }
        if (!await db.AccessGrants.AsNoTracking().AnyAsync(x => x.Id == grant.Id && x.IsActive && x.Revision == 1))
            throw new InvalidOperationException("Partial group retirement survived failed audit.");

        await using var first = new WellSiteAutoPilotDbContext(options);
        await using var second = new WellSiteAutoPilotDbContext(options);
        var outcomes = await Task.WhenAll(RetireAsync(first), RetireAsync(second));
        if (outcomes.Count(x => x) != 1 || await db.AuditEvents.CountAsync(x => x.Action == "security.grant.deactivated" && x.TargetId == grant.Id.ToString()) != 1)
            throw new InvalidOperationException("Concurrent group retirement had multiple winners or audits.");
        var retired = (await repository.ListAsync(group)).Single(x => x.Id == grant.Id);
        if (retired.IsActive || retired.Revision != 2) throw new InvalidOperationException("Group retirement revision lost.");
        foreach (var action in new[] { "security.grant.created", "security.grant.deactivated" })
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(x => x.Action == action && x.TargetId == grant.Id.ToString());
            using var details = JsonDocument.Parse(audit.DetailsJson);
            if (audit.ActorUserId != actor.UserId || audit.CorrelationId != actor.CorrelationId ||
                details.RootElement.GetProperty("subjectKind").GetString() != "WindowsGroup" ||
                details.RootElement.GetProperty("groupSid").GetString() != group.GroupSid ||
                details.RootElement.GetProperty("userId").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Group mutation audit lost subject or actor.");
        }
        // Concurrent re-creation after retirement: exactly one fresh active row.
        await using var creatorOne = new WellSiteAutoPilotDbContext(options);
        await using var creatorTwo = new WellSiteAutoPilotDbContext(options);
        var created = await Task.WhenAll(CreateAsync(creatorOne), CreateAsync(creatorTwo));
        if (created.Count(x => x) != 1 || await db.AccessGrants.CountAsync(x => x.GroupSid == group.GroupSid && x.IsActive) != 1)
            throw new InvalidOperationException("Concurrent group creation duplicated active assignment.");
        var remaining = (await repository.ListAsync(group)).Single(x => x.IsActive);
        await service.DeactivateAsync(group, remaining.Id, remaining.Revision, actor);
        Console.WriteLine("Windows-group administration canonical SID, isolation, custom role, scopes, audit atomicity and concurrent writes passed.");

        async Task<bool> RetireAsync(WellSiteAutoPilotDbContext writer)
        {
            try { await new AccessGrantRepository(writer).DeactivateAsync(group, grant.Id, 1, actor, clock.GetUtcNow()); return true; }
            catch (WellSiteAutoPilotException e) when (e.Kind == FailureKind.Conflict) { return false; }
        }
        async Task<bool> CreateAsync(WellSiteAutoPilotDbContext writer)
        {
            try { await new AccessGrantRepository(writer).CreateAsync(group, command, actor, clock.GetUtcNow()); return true; }
            catch (WellSiteAutoPilotException e) when (e.Kind == FailureKind.Conflict) { return false; }
        }
    }

    private static async Task ExpectAsync(Func<Task> action, FailureKind kind)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException e) when (e.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected {kind} denial.");
    }
}
