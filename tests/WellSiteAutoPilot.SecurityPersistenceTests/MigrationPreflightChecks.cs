using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static class MigrationPreflightChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile subject, UserAccessProfile demoted, UserAccessProfile administrator,
        Guid assetId, TimeProvider clock)
    {
        const string userAction = "security.migration.user-preflight-inspected";
        const string deploymentAction = "security.migration.deployment-preflight-inspected";
        await using var db = new WellSiteAutoPilotDbContext(options);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "preflight-regression");
        var beforeUser = await new UserAccessRepository(db).GetAsync(subject.Id);
        var beforeGrants = await db.AccessGrants.CountAsync();
        var beforeAudits = await db.AuditEvents.CountAsync();
        var snapshots = new LegacyMigrationSnapshotRepository(db);
        var deploymentSnapshots = new DeploymentMigrationSnapshotRepository(db);
        var audit = new LegacyMigrationPreflightAuditRepository(db);
        var (single, deployment) = Services(db);
        var snapshot = await snapshots.CaptureAsync(subject.Id, administrator.Id);
        var userResult = await single.InspectAsync(subject.Id, actor);
        var deploymentResult = await deployment.InspectAsync(actor);
        var expected = LegacyMigrationPreflightEvaluator.Evaluate(snapshot);
        if (!userResult.IsDiagnosticOnly || !deploymentResult.IsDiagnosticOnly ||
            userResult.ExpansionCount != expected.ExpansionCount || userResult.ReductionCount != expected.ReductionCount ||
            userResult.ActiveAssetCount != await db.Assets.CountAsync(item => item.IsActive) ||
            deploymentResult.ActiveUserCount != await db.Users.CountAsync(item => item.IsActive) ||
            deploymentResult.Users.Count != deploymentResult.ActiveUserCount ||
            deploymentResult.ExpansionCount != deploymentResult.Users.Sum(item => item.ExpansionCount) ||
            deploymentResult.ReductionCount != deploymentResult.Users.Sum(item => item.ReductionCount) ||
            !deploymentResult.Users.Select(item => item.UserId).SequenceEqual(
                deploymentResult.Users.Select(item => item.UserId).OrderBy(id => id)))
        {
            throw new InvalidOperationException("Preflight returned an incomplete or inconsistent diagnostic.");
        }
        foreach (var action in new[] { userAction, deploymentAction })
        {
            var recorded = await db.AuditEvents.AsNoTracking().SingleAsync(item =>
                item.Action == action && item.CorrelationId == actor.CorrelationId);
            if (recorded.ActorUserId != administrator.Id || recorded.ActorIdentity != actor.IdentityName ||
                recorded.OccurredAtUtc != clock.GetUtcNow() || recorded.AssetId is not null ||
                recorded.TargetId != (action == userAction ? subject.Id.ToString("D") : "deployment"))
            {
                throw new InvalidOperationException("Preflight audit lost its actor, time or target.");
            }
            using var details = JsonDocument.Parse(recorded.DetailsJson);
            if (!details.RootElement.GetProperty("diagnosticOnly").GetBoolean() ||
                !details.RootElement.GetProperty("liveAuthorizationUnchanged").GetBoolean() ||
                details.RootElement.TryGetProperty("users", out _) ||
                details.RootElement.TryGetProperty("differenceSamples", out _))
            {
                throw new InvalidOperationException("Preflight audit stored excessive or misleading details.");
            }
        }
        foreach (var rejected in new[] { subject, demoted })
        {
            var denied = new SecurityActorContext(rejected.Id, rejected.IdentityName, "denied-preflight");
            await RejectAsync(() => single.InspectAsync(subject.Id, denied), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => deployment.InspectAsync(denied), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => snapshots.CaptureAsync(subject.Id, rejected.Id), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => deploymentSnapshots.CaptureAsync(rejected.Id), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => audit.RecordUserAsync(userResult, denied, clock.GetUtcNow()), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => audit.RecordDeploymentAsync(deploymentResult, denied, clock.GetUtcNow()), "SECURITY_ADMIN_REQUIRED");
        }
        await RejectAsync(() => single.InspectAsync(Guid.NewGuid(), actor), "SECURITY_USER_NOT_ACTIVE");
        await RejectAsync(() => single.InspectAsync(Guid.Empty, actor), "SECURITY_MIGRATION_PREFLIGHT_INVALID");

        // A real storage failure must prevent either diagnostic from returning.
        foreach (var inspectDeployment in new[] { false, true })
        {
            await using var failureDb = new WellSiteAutoPilotDbContext(options);
            var (failedSingle, failedDeployment) = Services(failureDb);
            var badActor = actor with { IdentityName = new string('x', 257) };
            try
            {
                if (inspectDeployment) await failedDeployment.InspectAsync(badActor);
                else await failedSingle.InspectAsync(subject.Id, badActor);
                throw new InvalidOperationException("Preflight escaped without its required durable audit.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "22001" })
            {
            }
        }
        var afterUser = await new UserAccessRepository(db).GetAsync(subject.Id);
        if (await db.AuditEvents.CountAsync() != beforeAudits + 2 ||
            await db.AccessGrants.CountAsync() != beforeGrants || beforeUser is null || afterUser is null ||
            !afterUser.Roles.SequenceEqual(beforeUser.Roles) ||
            !afterUser.AssetScopeIds.SequenceEqual(beforeUser.AssetScopeIds))
        {
            throw new InvalidOperationException("Preflight mutated access or left a partial audit.");
        }

        // A concurrent committed Asset change cannot split one deployment
        // snapshot into before/after inventories. Restore the fixture even
        // when the assertion fails; no production authorization is changed.
        await using (var stable = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        await using (var concurrent = new WellSiteAutoPilotDbContext(options))
        {
            var first = await deploymentSnapshots.CaptureInExistingTransactionAsync(administrator.Id);
            try
            {
                await concurrent.Assets.Where(item => item.Id == assetId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
                var second = await deploymentSnapshots.CaptureInExistingTransactionAsync(administrator.Id);
                if (second.ActiveAssetCount != first.ActiveAssetCount ||
                    first.ActiveUsers.Any(item => !item.ActiveAssetIds.Contains(assetId)) ||
                    second.ActiveUsers.Any(item => !item.ActiveAssetIds.Contains(assetId)))
                {
                    throw new InvalidOperationException("Deployment snapshot observed a concurrent partial inventory change.");
                }
            }
            finally
            {
                await stable.RollbackAsync();
                await concurrent.Assets.Where(item => item.Id == assetId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, true));
            }
        }

        // The shared capture refuses weaker or absent isolation. These exact
        // boundaries also include inactive accounts, not just active users.
        await RequireIsolationFailureAsync();
        await using (var weak = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await RequireIsolationFailureAsync();
            await weak.RollbackAsync();
        }
        foreach (var kind in new[] { "users", "roles", "assets" })
        {
            await using var boundaryDb = new WellSiteAutoPilotDbContext(options);
            await using var tx = await boundaryDb.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var repository = new DeploymentMigrationSnapshotRepository(boundaryDb);
            var now = clock.GetUtcNow();
            var missing = kind switch
            {
                "users" => DeploymentMigrationSnapshotRepository.MaximumUsers - await boundaryDb.Users.CountAsync(),
                "roles" => LegacyMigrationSnapshotRepository.MaximumRoles - await boundaryDb.RoleDefinitions.CountAsync(),
                _ => LegacyMigrationSnapshotRepository.MaximumActiveAssets - await boundaryDb.Assets.CountAsync(item => item.IsActive)
            };
            var assetTypeId = await boundaryDb.Assets.Where(item => item.Id == assetId)
                .Select(item => item.AssetTypeId).SingleAsync();
            for (var index = 0; index < missing; index++) AddFixture();
            await boundaryDb.SaveChangesAsync();
            _ = await repository.CaptureInExistingTransactionAsync(administrator.Id);
            AddFixture();
            await boundaryDb.SaveChangesAsync();
            await RejectAsync(() => repository.CaptureInExistingTransactionAsync(administrator.Id),
                "SECURITY_MIGRATION_PREFLIGHT_TOO_LARGE");
            await tx.RollbackAsync();

            void AddFixture()
            {
                var key = Guid.NewGuid().ToString("N");
                if (kind == "users") boundaryDb.Users.Add(new UserEntity
                {
                    Id = Guid.NewGuid(), IdentityKey = key, IdentityName = key,
                    NormalizedIdentityName = key.ToUpperInvariant(), IsActive = false,
                    CreatedAtUtc = now, LastSeenAtUtc = now
                });
                else if (kind == "roles") boundaryDb.RoleDefinitions.Add(new RoleDefinitionEntity
                {
                    Id = Guid.NewGuid(), Key = key, Name = key, NormalizedName = key.ToUpperInvariant(),
                    IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
                });
                else boundaryDb.Assets.Add(new AssetEntity
                {
                    Id = Guid.NewGuid(), AssetTypeId = assetTypeId, Name = key, IsActive = true, CreatedAtUtc = now
                });
            }
        }

        async Task RequireIsolationFailureAsync()
        {
            try
            {
                await deploymentSnapshots.CaptureInExistingTransactionAsync(administrator.Id);
                throw new InvalidOperationException("Weak isolation was accepted for deployment preflight.");
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("requires RepeatableRead", StringComparison.Ordinal))
            {
            }
        }

        (LegacyMigrationPreflightService, DeploymentMigrationPreflightService) Services(WellSiteAutoPilotDbContext context)
        {
            var users = new UserAccessRepository(context);
            var administration = new AccessGrantAdministrationService(new AccessGrantRepository(context),
                users, new RoleCatalogRepository(context), clock);
            var writer = new LegacyMigrationPreflightAuditRepository(context);
            return (new LegacyMigrationPreflightService(administration,
                    new LegacyMigrationSnapshotRepository(context), writer, clock),
                new DeploymentMigrationPreflightService(administration,
                    new DeploymentMigrationSnapshotRepository(context), writer, clock));
        }
    }

    private static async Task RejectAsync(Func<Task> action, string code)
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Unsafe preflight accepted: {code}.");
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == code)
        {
        }
    }
}
