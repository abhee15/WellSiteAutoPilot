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

