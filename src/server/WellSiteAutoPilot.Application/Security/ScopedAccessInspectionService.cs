using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Read-only, diagnostic comparison. This model never controls live
/// authorization and never claims deployment-wide migration parity from one
/// Asset sample. Windows group grants remain unsupported by the evaluator.
/// </summary>
public sealed record PermissionAccessComparison(
    string Permission,
    string ScopeKind,
    bool LegacyAllowed,
    bool ProposedAllowed,
    string ProposedReasonCode,
    Guid? ProposedGrantId);

public sealed record ScopedAccessInspection(
    Guid UserId,
    Guid? AssetId,
    string? AssetName,
    IReadOnlyCollection<PermissionAccessComparison> Permissions,
    int WideningCount,
    int ReductionCount,
    bool IsDiagnosticOnly = true);

public static class ScopedAccessComparisonEvaluator
{
    public static ScopedAccessInspection Compare(
        UserAccessProfile user,
        Guid? targetAssetId,
        string? assetName,
        IReadOnlySet<Guid> activeAssetIds,
        IReadOnlyCollection<AccessGrant> grants,
        IReadOnlyCollection<RoleDefinition> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(activeAssetIds);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(roles);
        if (targetAssetId is { } id &&
            (id == Guid.Empty || !activeAssetIds.Contains(id)))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_INSPECTION_ASSET_INACTIVE", FailureKind.NotFound,
                "The selected Asset does not exist or is inactive.");
        }

        var entries = ProductPermissionCatalog.Definitions
            .Where(definition => definition.ScopeKind == "DeploymentWide" ||
                targetAssetId is not null)
            .Select(definition =>
            {
                var scoped = definition.ScopeKind == "ResourceScoped";
                var decision = ScopedGrantEvaluator.Authorize(
                    user.Id, user.IsActive, definition.Key,
                    scoped ? targetAssetId : null,
                    activeAssetIds, grants, roles);
                var legacy = user.IsActive &&
                    RolePermissionMap.HasPermission(user.Roles, definition.Key) &&
                    (!scoped || (targetAssetId is { } assetId &&
                        user.HasAssetAccess(assetId)));
                return new PermissionAccessComparison(
                    definition.Key, definition.ScopeKind, legacy,
                    decision.Allowed, decision.ReasonCode,
                    decision.GrantedByAssignmentId);
            }).ToArray();
        return new ScopedAccessInspection(
            user.Id, targetAssetId, assetName, entries,
            entries.Count(item => item.ProposedAllowed && !item.LegacyAllowed),
            entries.Count(item => item.LegacyAllowed && !item.ProposedAllowed));
    }
}

public sealed class ScopedAccessInspectionService(
    AccessGrantAdministrationService administration,
    IUserAccessRepository users,
    IAccessGrantRepository grants,
    IRoleCatalogRepository roles,
    IAssetRepository assets,
    IProposedAssetAccessRepository sqlProjections)
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

        return inspection;
    }
}
