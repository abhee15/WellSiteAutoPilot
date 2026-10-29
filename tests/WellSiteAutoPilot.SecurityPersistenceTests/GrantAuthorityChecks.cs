using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

internal static class GrantAuthorityChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        var scenario = 0;
        foreach (var group in new[] { false, true })
        foreach (var global in new[] { false, true })
        foreach (var revoke in global ? new[] { "user", "grant", "role", "permission" }
                     : new[] { "user", "grant", "role", "permission", "binding", "asset" })
        {
            await using var setup = new WellSiteAutoPilotDbContext(options);
            var now = DateTimeOffset.UtcNow;
            var groupId = $"S-1-5-21-909-{++scenario}";
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var grantId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var permission = global ? WellSitePermissions.LogicManage : WellSitePermissions.AssetsManage;
            var identity = "grant-fence-" + userId.ToString("N");
            setup.Users.Add(new UserEntity
            {
                Id = userId, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
                IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
            });
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = roleId, Key = "custom-" + roleId.ToString("N"), Name = identity,
                NormalizedName = identity, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = permission });
            setup.Assets.Add(new AssetEntity
            {
                Id = assetId, AssetTypeId = assetTypeId, Name = identity, IsActive = true, CreatedAtUtc = now
            });
            setup.AccessGrants.Add(new AccessGrantEntity
            {
                Id = grantId, SubjectKind = group ? "Group" : "User", UserId = group ? null : userId,
                GroupId = group ? groupId : null, RoleId = roleId,
                ScopeKind = global ? "DeploymentWide" : "ExactAssets", IsActive = true, Revision = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            if (!global) setup.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = grantId, AssetId = assetId });
            await setup.SaveChangesAsync();
            var subject = new AccessSubject(userId, group ? new VerifiedWindowsGroupMembership(userId, [groupId], now) : null);
            var clock = new FixedClock(now);
            await using var writer = new WellSiteAutoPilotDbContext(options);
            try { await AuthorizeAsync(writer); throw new InvalidOperationException("Grant guard allowed a missing transaction."); }
            catch (InvalidOperationException e) when (e.Message == "Grant mutation authorization requires a transaction.") { }

            await using var transaction = await writer.Database.BeginTransactionAsync();
            await AuthorizeAsync(writer);
            var auditId = Guid.NewGuid();
            writer.AuditEvents.Add(new AuditEventEntity
            {
                Id = auditId, ActorUserId = userId, ActorIdentity = identity,
                OccurredAtUtc = now, Action = "security.grant-fence.probe", TargetType = "security-user",
                TargetId = userId.ToString(), DetailsJson = "{}"
            });
            await writer.SaveChangesAsync();

            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "wsa-grant-fence-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var revoker = new NpgsqlConnection(connection.ConnectionString);
            await revoker.OpenAsync();
            var (sql, id) = revoke switch
            {
                "user" => ("UPDATE security.users SET \"IsActive\" = FALSE WHERE \"Id\" = $1", userId),
                "grant" => ("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", grantId),
                "role" => ("UPDATE security.role_definitions SET \"IsActive\" = FALSE WHERE \"Id\" = $1", roleId),
                "permission" => ("DELETE FROM security.role_permissions WHERE \"RoleId\" = $1", roleId),
                "binding" => ("DELETE FROM security.assignment_asset_scopes WHERE \"GrantId\" = $1", grantId),
                "asset" => ("UPDATE assets.assets SET \"IsActive\" = FALSE WHERE \"Id\" = $1", assetId),
                _ => throw new InvalidOperationException("Unknown fixture revocation.")
            };
            await using var command = new NpgsqlCommand(sql, revoker);
            command.Parameters.AddWithValue(id);
            var revocation = command.ExecuteNonQueryAsync();
            try
            {
                await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
                await observer.OpenAsync();
                await using var waiting = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE application_name = $1 AND wait_event_type = 'Lock'
                    AND cardinality(pg_blocking_pids(pid)) > 0)
                    """, observer);
                waiting.Parameters.AddWithValue(connection.ApplicationName!);
                var timer = Stopwatch.StartNew();
                while (!Equals(await waiting.ExecuteScalarAsync(), true))
                {
                    if (revocation.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                        throw new InvalidOperationException($"Grant fence missed {group}/{global}/{revoke} revocation.");
                    await Task.Delay(20);
                }
                await transaction.CommitAsync();
            }
            finally { await transaction.DisposeAsync(); }
            if (await revocation != 1 || !await setup.AuditEvents.AnyAsync(audit => audit.Id == auditId))
                throw new InvalidOperationException("Authorized write/audit or subsequent revocation was lost.");

            await using var denied = new WellSiteAutoPilotDbContext(options);
            await using var deniedTransaction = await denied.Database.BeginTransactionAsync();
            try { await ExpectDeniedAsync(() => AuthorizeAsync(denied)); }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Post-revocation check failed for {group}/{global}/{revoke}.", exception);
            }
            await deniedTransaction.RollbackAsync();
            if (group)
            {
                // A proof expiring while locks are acquired must be checked
                // again before the caller can mutate anything.
                await using var fresh = new WellSiteAutoPilotDbContext(options);
                // Restore only this fixture's revoked row in a rolled-back
                // transaction; no expired-proof test can leave authority behind.
                await using var freshTransaction = await fresh.Database.BeginTransactionAsync();
                if (revoke == "grant")
                {
                    await fresh.AccessGrants.Where(grant => grant.Id == grantId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, true));
                    await ExpectDeniedAsync(() => global
                        ? GrantWriteGuard.RequireDeploymentAsync(fresh, subject, permission, new ExpiringClock(now))
                        : GrantWriteGuard.RequireAssetsAsync(fresh, subject, permission, [assetId], new ExpiringClock(now)));
                }
                await freshTransaction.RollbackAsync();
            }

            // These deliberately active/inactive group fixtures must not
            // become input to the following whole-deployment legacy checks.
            await setup.AuditEvents.Where(audit => audit.Id == auditId).ExecuteDeleteAsync();
            await setup.AccessGrants.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();
            await setup.RolePermissions.Where(item => item.RoleId == roleId).ExecuteDeleteAsync();
            await setup.RoleDefinitions.Where(role => role.Id == roleId).ExecuteDeleteAsync();
            await setup.Assets.Where(asset => asset.Id == assetId).ExecuteDeleteAsync();
            await setup.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();

            Task AuthorizeAsync(WellSiteAutoPilotDbContext db) => global
                ? GrantWriteGuard.RequireDeploymentAsync(db, subject, permission, clock)
                : GrantWriteGuard.RequireAssetsAsync(db, subject, permission, [assetId], clock);
        }
        Console.WriteLine("Grant authority: 20 direct/group resource/deployment PostgreSQL revocation lock waits, post-revocation refusals and proof-expiry checks passed.");
    }

    private static async Task ExpectDeniedAsync(Func<Task> action)
    {
        try { await action(); throw new InvalidOperationException("Revoked or expired grant authorized a mutation."); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Authorization) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ExpiringClock(DateTimeOffset now) : TimeProvider
    {
        private bool observed;
        public override DateTimeOffset GetUtcNow()
        {
            if (observed) return now.AddMinutes(2);
            observed = true;
            return now;
        }
    }
}
