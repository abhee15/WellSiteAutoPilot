using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class AdministratorRevocationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile administrator, UserAccessProfile target, Guid assetId, TimeProvider clock)
    {
        foreach (var operation in new[] { "role-create", "role-update", "grant-create", "grant-retire" })
        foreach (var revokeUser in new[] { false, true })
        {
            await using var setup = new WellSiteAutoPilotDbContext(options);
            var identity = "FENCE\\" + Guid.NewGuid().ToString("N");
            var actorUser = await new UserAccessRepository(setup).UpsertAuthenticatedUserAsync(
                identity, identity, identity.ToUpperInvariant(), null, clock.GetUtcNow());
            setup.UserRoles.Add(new UserRoleEntity { UserId = actorUser.Id, Role = "Admin" });
            await setup.SaveChangesAsync();
            var actor = new SecurityActorContext(actorUser.Id, identity, "admin-revocation-" + operation);
            var originalActor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "revocation-fixture");
            var role = await new RoleCatalogRepository(setup).CreateAsync(NewRole(), originalActor);
            var grantCommand = new CreateAccessGrantCommand(role.Id, AccessGrantScopeKind.ExactAssets, [assetId], role.Revision);
            AccessGrant? grant = null;
            if (operation == "grant-retire") grant = await new AccessGrantRepository(setup)
                .CreateForUserAsync(target.Id, grantCommand, originalActor, clock.GetUtcNow());

            var pause = new PauseMutationSave();
            var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
                .AddInterceptors(pause).Options;
            await using var writer = new WellSiteAutoPilotDbContext(pausedOptions);
            var mutation = MutateAsync(writer);
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "wsa-revocation-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var revoker = new NpgsqlConnection(connection.ConnectionString);
            await revoker.OpenAsync();
            await using var command = new NpgsqlCommand(revokeUser
                ? "UPDATE security.users SET \"IsActive\" = FALSE WHERE \"Id\" = $1"
                : "DELETE FROM security.user_roles WHERE \"UserId\" = $1 AND \"Role\" = 'Admin'", revoker);
            command.Parameters.AddWithValue(actorUser.Id);
            var revocation = command.ExecuteNonQueryAsync();
            try
            {
                // Observe an actual database lock wait; do not infer locking
                // merely because a task has not finished after an arbitrary delay.
                await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
                await observer.OpenAsync();
                await using var waiting = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE application_name = $1 AND wait_event_type = 'Lock'
                    AND cardinality(pg_blocking_pids(pid)) > 0)
                    """, observer);
                waiting.Parameters.AddWithValue(connection.ApplicationName!);
                var timeout = Stopwatch.StartNew();
                while (!Equals(await waiting.ExecuteScalarAsync(), true))
                {
                    if (revocation.IsCompleted || timeout.Elapsed > TimeSpan.FromSeconds(8))
                        throw new InvalidOperationException($"{operation} did not fence administrator revocation.");
                    await Task.Delay(20);
                }
            }
            finally { pause.Release.TrySetResult(); }
            await mutation;
            if (await revocation != 1) throw new InvalidOperationException("Fixture revocation did not affect one authority row.");

            var audits = await setup.AuditEvents.CountAsync();
            var roles = await setup.RoleDefinitions.CountAsync();
            var grants = await setup.AccessGrants.CountAsync();
            // The actor ID and prior profile still exist, but authority has
            // now gone. Direct repository calls must reject it without writes.
            await using var denied = new WellSiteAutoPilotDbContext(options);
            try { await MutateAsync(denied); throw new InvalidOperationException("Revoked actor changed security state."); }
            catch (WellSiteAutoPilotException e) when (e.Kind == FailureKind.Authorization) { }
            if (audits != await setup.AuditEvents.CountAsync() || roles != await setup.RoleDefinitions.CountAsync() ||
                grants != await setup.AccessGrants.CountAsync())
                throw new InvalidOperationException("Revoked actor left a role, grant or audit mutation.");

            async Task MutateAsync(WellSiteAutoPilotDbContext db)
            {
                switch (operation)
                {
                    case "role-create": await new RoleCatalogRepository(db).CreateAsync(NewRole(), actor); break;
                    case "role-update": await new RoleCatalogRepository(db).UpdateAsync(role.Id, role.Revision,
                        new RoleMutationCommand(role.Name, "Updated under authority lock", role.Permissions, true), actor, clock.GetUtcNow()); break;
                    case "grant-create": await new AccessGrantRepository(db).CreateForUserAsync(target.Id, grantCommand, actor, clock.GetUtcNow()); break;
                    case "grant-retire": await new AccessGrantRepository(db).DeactivateForUserAsync(target.Id, grant!.Id, grant.Revision, actor, clock.GetUtcNow()); break;
                }
            }
            RoleDefinition NewRole()
            {
                var id = Guid.NewGuid();
                return new(id, "custom-" + id.ToString("N"), "Fence " + id.ToString("N"), "", false, true, 1,
                    clock.GetUtcNow(), clock.GetUtcNow(), [WellSitePermissions.AssetsRead]);
            }
        }
        Console.WriteLine("Role/grant writes hold administrator authority until commit; real role-removal and user-deactivation lock waits and post-revocation refusals passed.");
    }

    private sealed class PauseMutationSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
