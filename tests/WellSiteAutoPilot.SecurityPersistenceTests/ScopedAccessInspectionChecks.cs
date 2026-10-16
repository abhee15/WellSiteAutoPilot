using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static class ScopedAccessInspectionChecks
{
    public static async Task RunAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile subject, UserAccessProfile demotedAdministrator,
        UserAccessProfile administrator, Guid assetId, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var users = new UserAccessRepository(db);
        var legacy = new UserAccessService(users, clock);
        var roles = new RoleCatalogRepository(db);
        var catalog = new RoleCatalogService(roles, users, clock);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "inspection-regression");
        var role = await catalog.CreateAsync(new RoleMutationCommand("Inspection Regression", "",
            [WellSitePermissions.AssetsManage], true), actor);
        var administration = new AccessGrantAdministrationService(new AccessGrantRepository(db), users, roles, clock);
        var before = await legacy.GetRequiredAsync(subject.Id);
        var grant = await administration.CreateForUserAsync(subject.Id,
            new CreateAccessGrantCommand(role.Id, AccessGrantScopeKind.ExactAssets, [assetId], role.Revision), actor);
        var inspector = Inspector(db, new ProposedAssetAccessRepository(db));
        var auditCount = await db.AuditEvents.CountAsync(item => item.Action == "security.access.inspected");
        var result = await inspector.InspectAsync(subject.Id, assetId, actor);
        if (!result.IsDiagnosticOnly || result.WideningCount < 1 ||
            !result.Permissions.Single(item => item.Permission == WellSitePermissions.AssetsManage).ProposedAllowed)
        {
            throw new InvalidOperationException("Server inspection failed to expose a draft privilege expansion.");
        }
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(item =>
            item.Action == "security.access.inspected" && item.CorrelationId == actor.CorrelationId);
        if (audit.ActorUserId != administrator.Id || audit.ActorIdentity != administrator.IdentityName ||
            audit.TargetId != subject.Id.ToString("D") || audit.AssetId != assetId ||
            audit.OccurredAtUtc != clock.GetUtcNow() ||
            await db.AuditEvents.CountAsync(item => item.Action == "security.access.inspected") != auditCount + 1)
        {
            throw new InvalidOperationException("Privileged inspection was not durably audited with its actor and scope.");
        }
        using var details = JsonDocument.Parse(audit.DetailsJson);
        if (!details.RootElement.GetProperty("diagnosticOnly").GetBoolean() ||
            !details.RootElement.GetProperty("liveAuthorizationUnchanged").GetBoolean() ||
            details.RootElement.GetProperty("permissionCodes").GetArrayLength() != result.Permissions.Count ||
            details.RootElement.TryGetProperty("assetName", out _) ||
            details.RootElement.TryGetProperty("grants", out _))
        {
            throw new InvalidOperationException("Inspection audit retained an invalid or over-broad payload.");
        }

        await RejectAsync(() => inspector.InspectAsync(subject.Id, assetId,
            new SecurityActorContext(subject.Id, subject.IdentityName, "operator-inspection")), "SECURITY_ADMIN_REQUIRED");
        await RejectAsync(() => inspector.InspectAsync(subject.Id, assetId,
            new SecurityActorContext(demotedAdministrator.Id, demotedAdministrator.IdentityName, "demoted-inspection")),
            "SECURITY_ADMIN_REQUIRED");
        await RejectAsync(() => inspector.InspectAsync(Guid.NewGuid(), assetId, actor), "SECURITY_USER_NOT_ACTIVE");
        await RejectAsync(() => inspector.InspectAsync(subject.Id, Guid.NewGuid(), actor), "SECURITY_INSPECTION_ASSET_INACTIVE");
        await RejectAsync(() => Inspector(db, new DenyingProjection()).InspectAsync(subject.Id, assetId, actor),
            "SECURITY_INSPECTION_SQL_MISMATCH");
        if (await db.AuditEvents.CountAsync(item => item.Action == "security.access.inspected") != auditCount + 1)
        {
            throw new InvalidOperationException("Denied or inconsistent inspection recorded a successful result.");
        }

        // The persistence boundary revalidates the current administrator too.
        await RejectAsync(() => new ScopedAccessInspectionAuditRepository(db).RecordAsync(result,
            new SecurityActorContext(demotedAdministrator.Id, demotedAdministrator.IdentityName, "direct-inspection"),
            clock.GetUtcNow()), "SECURITY_ADMIN_REQUIRED");

        // Force a real PostgreSQL audit failure. No inspection result may
        // escape to the caller when the mandatory audit could not commit.
        var totalAudits = await db.AuditEvents.CountAsync();
        await using (var failedAudit = new WellSiteAutoPilotDbContext(options))
        {
            try
            {
                await Inspector(failedAudit, new ProposedAssetAccessRepository(failedAudit)).InspectAsync(
                    subject.Id, assetId, new SecurityActorContext(administrator.Id, new string('x', 257), "audit-failure"));
                throw new InvalidOperationException("Inspection escaped without a committed audit.");
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: "22001" })
            {
            }
        }
        var after = await legacy.GetRequiredAsync(subject.Id);
        if (await db.AuditEvents.CountAsync() != totalAudits ||
            !after.Roles.SequenceEqual(before.Roles) || !after.AssetScopeIds.SequenceEqual(before.AssetScopeIds))
        {
            throw new InvalidOperationException("Inspection failure left a partial audit or changed legacy access.");
        }
        await administration.DeactivateForUserAsync(subject.Id, grant.Id, grant.Revision, actor);

        ScopedAccessInspectionService Inspector(WellSiteAutoPilotDbContext context, IProposedAssetAccessRepository projection)
        {
            var userRepository = new UserAccessRepository(context);
            var roleRepository = new RoleCatalogRepository(context);
            var grants = new AccessGrantRepository(context);
            return new ScopedAccessInspectionService(
                new AccessGrantAdministrationService(grants, userRepository, roleRepository, clock),
                userRepository, grants, roleRepository, new AssetRepository(context), projection,
                new ScopedAccessInspectionAuditRepository(context), clock);
        }
    }

    private static async Task RejectAsync(Func<Task> action, string code)
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Invalid inspection accepted: {code}.");
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == code)
        {
        }
    }

    private sealed class DenyingProjection : IProposedAssetAccessRepository
    {
        public Task<bool> IsAssetAuthorizedAsync(Guid userId, string permission, Guid assetId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> AreAssetsAuthorizedAsync(Guid userId, string permission, IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsDeploymentPermissionGrantedAsync(Guid userId, string permission,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyCollection<ProposedAuthorizedAsset>> ListAsync(Guid userId, string permission,
            int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<ProposedAuthorizedAsset>>([]);
    }
}
