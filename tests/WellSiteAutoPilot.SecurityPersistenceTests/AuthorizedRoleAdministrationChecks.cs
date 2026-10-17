using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedRoleAdministrationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid directAdministrator,
        AccessSubject groupAdministrator, Guid administratorGrant, Guid protectedRole)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var direct = new AccessSubject(directAdministrator);
        var directActor = new SecurityActorContext(directAdministrator, "direct-grant-admin", "grant-role-probe");
        var groupActor = new SecurityActorContext(groupAdministrator.UserId, "group-grant-admin", "grant-role-probe");
        var name = "Role probe " + Guid.NewGuid().ToString("N");
        var command = new RoleMutationCommand(name, "Grant administration", [WellSitePermissions.AssetsRead]);
        var created = await CreateAsync(command, direct, directActor);
        var groupCreated = await CreateAsync(command with { Name = name + " group" }, groupAdministrator, groupActor);
        if (await setup.UserRoles.AnyAsync()) throw new InvalidOperationException("Role authoring depended on legacy Admin assignments.");
        await ExpectAsync(() => CreateAsync(command with { Name = name + " custom" }, new(groupAdministrator.UserId), groupActor), FailureKind.Authorization);
        await ExpectAsync(() => CreateAsync(command with { Name = name + " actor" }, direct, groupActor), FailureKind.Authorization);
        var renamed = await UpdateAsync(created.Id, created.Revision, command with { Name = name + " renamed" }, direct, directActor);
        await ExpectAsync(() => UpdateAsync(created.Id, created.Revision, command, direct, directActor), FailureKind.Conflict);
        await ExpectAsync(() => UpdateAsync(protectedRole, 1, command with { Name = name + " system" }, direct, directActor), FailureKind.Authorization);
        var own = Guid.NewGuid();
        await AddAssignmentAsync(own, directAdministrator, null);
        await ExpectAsync(() => UpdateAsync(created.Id, renamed.Revision, command with { IsActive = false }, direct, directActor), FailureKind.Authorization);
        await setup.AccessGrants.Where(item => item.Id == own).ExecuteDeleteAsync();
        var ownGroup = Guid.NewGuid();
        await AddAssignmentAsync(ownGroup, null, groupAdministrator.WindowsGroups!.GroupSids.Single());
        await ExpectAsync(() => UpdateAsync(created.Id, renamed.Revision, command with { IsActive = false }, groupAdministrator, groupActor), FailureKind.Authorization);
        await setup.AccessGrants.Where(item => item.Id == ownGroup).ExecuteDeleteAsync();
        var assigned = Guid.NewGuid();
        await AddAssignmentAsync(assigned, groupAdministrator.UserId, null);
        await ExpectAsync(() => UpdateAsync(created.Id, renamed.Revision,
            command with { Permissions = [WellSitePermissions.AssetsManage] }, direct, directActor), FailureKind.Conflict);
        await setup.AccessGrants.Where(item => item.Id == assigned).ExecuteDeleteAsync();
        var disabled = await UpdateAsync(created.Id, renamed.Revision, command with { IsActive = false }, direct, directActor);
        var enabled = await UpdateAsync(created.Id, disabled.Revision, command with { Permissions = [WellSitePermissions.AssetsManage] }, direct, directActor);
        if (!enabled.IsActive || enabled.Permissions.Count != 1 || !enabled.Permissions.Contains(WellSitePermissions.AssetsManage))
            throw new InvalidOperationException("Unassigned role lifecycle lost its permission revision.");

        var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        try
        {
            await CreateAsync(command with { Name = name + " rollback" }, direct, directActor, failingOptions);
            throw new InvalidOperationException("Role failure injection did not run.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "role-save-probe") { }
        if (await setup.RoleDefinitions.AnyAsync(item => item.Name == name + " rollback") ||
            await setup.AuditEvents.CountAsync(item => item.CorrelationId == directActor.CorrelationId) != 5)
            throw new InvalidOperationException("Role creation failure did not roll back role/permission/audit together.");

        var pause = new PauseAfterSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        var write = CreateAsync(command with { Name = name + " concurrent" }, direct, directActor, pausedOptions);
        RoleDefinition concurrent;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "role-admin-revoke-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var revoker = new NpgsqlConnection(connection.ConnectionString);
            await revoker.OpenAsync();
            await using var revokeCommand = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
            revokeCommand.Parameters.AddWithValue(administratorGrant);
            var revoke = revokeCommand.ExecuteNonQueryAsync();
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
                    if (revoke.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                        throw new InvalidOperationException("Role mutation did not retain protected administrator authority through commit.");
                    await Task.Delay(20);
                }
            }
            finally { pause.Release.TrySetResult(); }
            concurrent = await write;
            if (await revoke != 1) throw new InvalidOperationException("Administrator revocation did not complete.");
        }
        finally { pause.Release.TrySetResult(); await write; }
        await ExpectAsync(() => CreateAsync(command with { Name = name + " revoked" }, direct, directActor), FailureKind.Authorization);
        await setup.AccessGrants.Where(item => item.Id == administratorGrant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        if (await setup.AuditEvents.CountAsync(item => item.CorrelationId == directActor.CorrelationId) != 6)
            throw new InvalidOperationException("Denied role mutation changed audit state.");
        var ids = new[] { created.Id, groupCreated.Id, concurrent.Id };
        await setup.RolePermissions.Where(item => ids.Contains(item.RoleId)).ExecuteDeleteAsync();
        await setup.RoleDefinitions.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        await setup.AuditEvents.Where(item => item.CorrelationId == directActor.CorrelationId).ExecuteDeleteAsync();
        Console.WriteLine("Protected grant role authoring supports direct/group administrators without legacy roles and retains own-access, assignment, revision, rollback and revocation protections.");

        async Task AddAssignmentAsync(Guid id, Guid? user, string? sid)
        {
            setup.AccessGrants.Add(new AccessGrantEntity
            {
                Id = id, RoleId = created.Id, SubjectKind = user is null ? "WindowsGroup" : "User",
                UserId = user, GroupSid = sid, ScopeKind = "AllAssets", IsActive = true, Revision = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }
        async Task<RoleDefinition> CreateAsync(RoleMutationCommand mutation, AccessSubject subject, SecurityActorContext actor,
            DbContextOptions<WellSiteAutoPilotDbContext>? writeOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(writeOptions ?? options);
            return await new RoleCatalogService(new RoleCatalogRepository(db), new UserAccessRepository(db), TimeProvider.System)
                .CreateAuthorizedAsync(mutation, subject, actor);
        }
        async Task<RoleDefinition> UpdateAsync(Guid id, long revision, RoleMutationCommand mutation, AccessSubject subject, SecurityActorContext actor)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new RoleCatalogService(new RoleCatalogRepository(db), new UserAccessRepository(db), TimeProvider.System)
                .UpdateAuthorizedAsync(id, revision, mutation, subject, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected role refusal: {kind}.");
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("role-save-probe");
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
