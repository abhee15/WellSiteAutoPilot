using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public static class RolePermissionMap
{
    private static readonly IReadOnlyDictionary<ApplicationRole, IReadOnlySet<string>>
        PermissionsByRole =
            new Dictionary<ApplicationRole, IReadOnlySet<string>>
            {
                [ApplicationRole.Admin] =
                    new HashSet<string>(
                        WellSitePermissions.All,
                        StringComparer.Ordinal),
                [ApplicationRole.Engineer] =
                    new HashSet<string>(
                        [
                            WellSitePermissions.AssetsRead,
                            WellSitePermissions.AssetsManage,
                            WellSitePermissions.LogicRead,
                            WellSitePermissions.LogicManage,
                            WellSitePermissions.ConfiguredLogicRead,
                            WellSitePermissions.ConfiguredLogicManage,
                            WellSitePermissions.ExecutionsRead,
                            WellSitePermissions.ExecutionsRequest,
                            WellSitePermissions.RecommendationsRead,
                            WellSitePermissions.RecommendationsDecide
                        ],
                        StringComparer.Ordinal),
                [ApplicationRole.Operator] =
                    new HashSet<string>(
                        [
                            WellSitePermissions.AssetsRead,
                            WellSitePermissions.LogicRead,
                            WellSitePermissions.ConfiguredLogicRead,
                            WellSitePermissions.ExecutionsRead,
                            WellSitePermissions.RecommendationsRead,
                            WellSitePermissions.RecommendationsDecide
                        ],
                        StringComparer.Ordinal)
            };

    public static bool HasPermission(
        IEnumerable<ApplicationRole> roles,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return roles.Any(
            role => PermissionsByRole.TryGetValue(role, out var permissions) &&
                    permissions.Contains(permission));
    }
}
