using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Pure migration rehearsal, never a database writer or runtime authorization
/// switch. It preserves the full legacy role x Asset-scope product, including
/// separating deployment-wide and resource privileges for mixed roles.
/// Passing here is NOT a cutover approval: a real migration still requires
/// an atomic authoritative Asset inventory and user snapshot.
/// </summary>
public static class LegacyGrantMigrationPlanner
{
    public static IReadOnlyCollection<AccessGrant> PreviewForKnownAssets(
        UserAccessProfile user,
        IReadOnlyCollection<RoleDefinition> systemRoles,
        IReadOnlySet<Guid> activeAssetIds)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(systemRoles);
        ArgumentNullException.ThrowIfNull(activeAssetIds);

        if (!user.IsActive || user.Id == Guid.Empty ||
            user.Roles.Count != user.Roles.Distinct().Count() ||
            user.AssetScopeIds.Count != user.AssetScopeIds.Distinct().Count() ||
            user.AssetScopeIds.Any(id => !activeAssetIds.Contains(id)) ||
            user.AssetScopeIds.Count > 500 || activeAssetIds.Count > 20000)
        {
            throw Invalid("Legacy user or Asset snapshot is invalid or incomplete.");
        }

        var roles = new List<RoleDefinition>();
        foreach (var kind in user.Roles)
        {
            var matches = systemRoles.Where(role => role.IsSystem && role.IsActive &&
                string.Equals(role.Name, kind.ToString(), StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1 || matches[0].Id == Guid.Empty)
            {
                throw Invalid("Missing or ambiguous built-in role mapping.");
            }

            var expected = ProductPermissionCatalog.Definitions
                .Select(item => item.Key)
                .Where(permission => RolePermissionMap.HasPermission(
                    [kind], permission)).ToHashSet(StringComparer.Ordinal);
            var actual = matches[0].Permissions.ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(expected) ||
                matches[0].Permissions.Count != actual.Count)
            {
                throw Invalid("System role permissions drifted from legacy authorization.");
            }
            roles.Add(matches[0]);
        }

        var planned = new List<AccessGrant>();
        foreach (var role in roles)
        {
            var global = role.Permissions.Any(permission =>
                ProductPermissionCatalog.Definitions.Any(item =>
                    item.Key == permission && item.ScopeKind == "DeploymentWide"));
            var resource = role.Permissions.Any(permission =>
                ProductPermissionCatalog.Definitions.Any(item =>
                    item.Key == permission && item.ScopeKind == "ResourceScoped"));
            // AllAssets belongs to the Admin grant itself, never to a
            // different role simply because the same user is an Admin.
            // Otherwise removing Admin later would leave Operator/Engineer
            // with unexpectedly unrestricted historical Asset rights.
            var administratorRole = role.Name == nameof(ApplicationRole.Admin);
            if (global)
            {
                planned.Add(Grant(role, AccessGrantScopeKind.DeploymentWide, []));
            }
            if (resource && administratorRole)
            {
                planned.Add(Grant(role, AccessGrantScopeKind.AllAssets, []));
            }
            else if (resource && user.AssetScopeIds.Count > 0)
            {
                planned.Add(Grant(role, AccessGrantScopeKind.ExactAssets,
                    user.AssetScopeIds.OrderBy(id => id).ToArray()));
            }
        }

        AccessGrant Grant(RoleDefinition role, AccessGrantScopeKind scope,
            IReadOnlyCollection<Guid> assets) =>
            new(Guid.NewGuid(), AccessGrantSubjectKind.User, user.Id,
                null, role.Id, scope, assets, true, 1);

        // Verify every known permission, deployment-wide and against each
        // ACTIVE Asset provided by the caller. No cross-grant union shortcut.
        Verify(null);
        foreach (var assetId in activeAssetIds) Verify(assetId);
        return planned;

        void Verify(Guid? target)
        {
            var comparison = ScopedAccessComparisonEvaluator.Compare(
                user, target, null, activeAssetIds, planned, roles);
            if (comparison.WideningCount != 0 || comparison.ReductionCount != 0)
            {
                throw Invalid("Legacy RBAC cannot be reproduced without drift.");
            }
        }
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("SECURITY_LEGACY_MIGRATION_PARITY_FAILED",
            FailureKind.Validation, message);
}
