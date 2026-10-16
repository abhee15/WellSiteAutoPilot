using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed class ScopedAccessInspectionService(
    AccessGrantAdministrationService administration,
    IUserAccessRepository users,
    IAccessGrantRepository grants,
    IRoleCatalogRepository roles,
    IAssetRepository assets,
    IProposedAssetAccessRepository sqlProjections,
    IScopedAccessInspectionAuditRepository audit,
    TimeProvider clock)
{
    public async Task<ScopedAccessInspection> InspectAsync(
        Guid userId, Guid? assetId, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await administration.RequireAdministratorAsync(actor, cancellationToken);
        if (userId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "A user ID is required.");
        }

        var user = await users.GetAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE", FailureKind.NotFound,
                "The user must be active for an access inspection.");
        }

        var assigned = await grants.ListForUserAsync(userId, cancellationToken);
        var roleIds = assigned.Where(item => item.IsActive)
            .Select(item => item.RoleId).Distinct().ToArray();
        var assignedRoles = new List<RoleDefinition>(roleIds.Length);
        foreach (var roleId in roleIds)
        {
            var role = await roles.GetAsync(roleId, cancellationToken);
            if (role is not null) assignedRoles.Add(role);
        }

        var ids = assigned.Where(item => item.IsActive)
            .SelectMany(item => item.AssetIds)
            .Concat(assetId is { } selected ? new[] { selected } : Array.Empty<Guid>())
            .Distinct().ToArray();
        if (ids.Length > 20000)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_INSPECTION_TOO_LARGE", FailureKind.Validation,
                "The assignment scope is too large for a verified inspection.");
        }

        // A bulk authoritative query prevents N+1 Asset reads. Inactive
        // selections are deliberately absent and invalidate matching grants.
        var active = await assets.GetActiveAssetIdsAsync(ids, cancellationToken);
        string? name = null;
        if (assetId is { } target)
        {
            if (!active.Contains(target))
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_INSPECTION_ASSET_INACTIVE", FailureKind.NotFound,
                    "The selected Asset does not exist or is inactive.");
            }
            name = (await assets.GetAssetAsync(target, cancellationToken))?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_INSPECTION_ASSET_INACTIVE", FailureKind.NotFound,
                    "The selected Asset is no longer available.");
            }
        }

        var inspection = ScopedAccessComparisonEvaluator.Compare(
            user, assetId, name, active, assigned, assignedRoles);

        // Compare the future runtime's SQL predicate with the pure evaluator
        // for every inspected permission. An inspector that returns reassuring
        // "parity" despite a disagreement in its two implementation paths
        // could incorrectly authorize a later staged cutover. Fail closed.
        foreach (var entry in inspection.Permissions)
        {
            var sqlAllowed = entry.ScopeKind == "DeploymentWide"
                ? await sqlProjections.IsDeploymentPermissionGrantedAsync(
                    userId, entry.Permission, cancellationToken)
                : await sqlProjections.IsAssetAuthorizedAsync(
                    userId, entry.Permission, assetId!.Value, cancellationToken);
            if (sqlAllowed != entry.ProposedAllowed)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_INSPECTION_SQL_MISMATCH", FailureKind.Conflict,
                    "The stored-grant SQL projection disagrees with the scoped evaluator. Authorization cutover is blocked.");
            }
        }

        await audit.RecordAsync(inspection, actor, clock.GetUtcNow(), cancellationToken);
        return inspection;
    }
}
