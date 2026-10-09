namespace WellSiteAutoPilot.Application.Security;

public static class WellSitePermissions
{
    public const string AssetsRead = "assets.read";
    public const string AssetsManage = "assets.manage";
    public const string LogicRead = "logic.read";
    public const string LogicManage = "logic.manage";
    public const string ConfiguredLogicRead = "configured-logic.read";
    public const string ConfiguredLogicManage = "configured-logic.manage";
    public const string ExecutionsRead = "executions.read";
    public const string ExecutionsRequest = "executions.request";
    public const string RecommendationsRead = "recommendations.read";
    public const string RecommendationsDecide = "recommendations.decide";
    public const string SecurityManage = "security.manage";

    public static IReadOnlyCollection<string> All { get; } =
    [
        AssetsRead,
        AssetsManage,
        LogicRead,
        LogicManage,
        ConfiguredLogicRead,
        ConfiguredLogicManage,
        ExecutionsRead,
        ExecutionsRequest,
        RecommendationsRead,
        RecommendationsDecide,
        SecurityManage
    ];
}
