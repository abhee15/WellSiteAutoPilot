using Microsoft.AspNetCore.Authorization;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Security;

public static class WellSitePolicies
{
    public const string AssetsRead = WellSitePermissions.AssetsRead;
    public const string AssetsManage = WellSitePermissions.AssetsManage;
    public const string LogicRead = WellSitePermissions.LogicRead;
    public const string LogicManage = WellSitePermissions.LogicManage;
    public const string ConfiguredLogicRead = WellSitePermissions.ConfiguredLogicRead;
    public const string ConfiguredLogicManage = WellSitePermissions.ConfiguredLogicManage;
    public const string ExecutionsRead = WellSitePermissions.ExecutionsRead;
    public const string ExecutionsRequest = WellSitePermissions.ExecutionsRequest;
    public const string RecommendationsRead = WellSitePermissions.RecommendationsRead;
    public const string RecommendationsDecide = WellSitePermissions.RecommendationsDecide;
    public const string SecurityManage = WellSitePermissions.SecurityManage;
    public const string AssetTypesManage = WellSitePermissions.AssetTypesManage;
    public const string AssetTypesHide = WellSitePermissions.AssetTypesHide;
    public const string ConnectionsRead = WellSitePermissions.ConnectionsRead;
    public const string ConnectionsManage = WellSitePermissions.ConnectionsManage;

    public static void AddGrantPolicies(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var permission in WellSitePermissions.All)
            options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new GrantPermissionRequirement(permission)));
    }

}
