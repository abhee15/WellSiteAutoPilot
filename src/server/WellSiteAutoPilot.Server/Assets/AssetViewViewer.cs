using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Security;

namespace WellSiteAutoPilot.Server.Assets;

/// <summary>
/// What one caller may do with Views and which Connections they can see, worked out once per request. A shared View
/// is described to each viewer from their own point of view; it never widens what they may see.
/// </summary>
internal sealed class AssetViewViewer
{
    private readonly Guid userId;
    private readonly bool mayShare;
    private readonly IReadOnlySet<Guid> visibleConnections;

    private AssetViewViewer(Guid userId, bool mayShare, IReadOnlySet<Guid> visibleConnections)
    {
        this.userId = userId;
        this.mayShare = mayShare;
        this.visibleConnections = visibleConnections;
    }

    public static async Task<AssetViewViewer> ForAsync(IRequestAccessRepository grants, IAssetRepository assets,
        AccessSubject subject, CancellationToken cancellationToken)
    {
        var held = await GrantPermissionChecker.HeldAsync(grants, subject, [WellSitePermissions.AssetViewsShare], cancellationToken);
        var owners = await assets.ListOwnersAuthorizedAsync(subject, cancellationToken);
        return new AssetViewViewer(subject.UserId, held.Contains(WellSitePermissions.AssetViewsShare),
            owners.Where(item => item.ConnectionId is not null).Select(item => item.ConnectionId!.Value).ToHashSet());
    }

    public AssetViewResponse Describe(AssetViewRecord view) =>
        AssetViewMapping.ToResponse(view, userId, mayShare, visibleConnections);

    /// <summary>A definition the viewer can see in full: someone else's keeps only the Connections this viewer can see.</summary>
    public AssetViewDefinition Visible(bool isOwner, AssetViewDefinition definition) => isOwner
        ? definition
        : definition with
        {
            Connections = definition.Connections.Where(item => item == AssetViewDefinitionRules.Standalone ||
                (Guid.TryParse(item, out var id) && visibleConnections.Contains(id))).ToArray()
        };

    public void RequireMayShare(AssetViewVisibility visibility)
    {
        if (visibility == AssetViewVisibility.Shared && !mayShare)
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.ShareForbidden, FailureKind.Authorization,
                "You do not have permission to share views.");
    }
}
