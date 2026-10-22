using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Changes;

/// <summary>
/// Wire names and the area to permission map. The hub route is deliberately outside the
/// reviewed v1 API group: it exposes no resource, only a "something changed" signal.
/// </summary>
public static class ChangeSignalProtocol
{
    public const string Route = "/hubs/changes";
    public const string ClientMethod = "changed";

    /// <summary>The read permission a user must currently hold to hear about an area.</summary>
    public static string ReadPermission(ChangeArea area) => area switch
    {
        ChangeArea.Executions => WellSitePermissions.ExecutionsRead,
        ChangeArea.Recommendations => WellSitePermissions.RecommendationsRead,
        ChangeArea.ConfiguredLogic => WellSitePermissions.ConfiguredLogicRead,
        ChangeArea.Assets => WellSitePermissions.AssetsRead,
        ChangeArea.Modules => WellSitePermissions.LogicRead,
        ChangeArea.Connections => WellSitePermissions.ConnectionsRead,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown change area.")
    };

    public static string GroupName(ChangeArea area) => "area:" + ChangeAreaNames.ToWireName(area);
}
