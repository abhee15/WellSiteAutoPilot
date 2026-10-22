using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Assets;

internal static class AssetViewMapping
{
    public static AssetViewResponse ToResponse(AssetViewRecord view, IReadOnlySet<string> held) =>
        new(view.Id, view.ConnectionId, view.Name, view.Revision, view.MemberCount, view.HasUnavailableMembers,
            view.CreatedAtUtc, view.UpdatedAtUtc,
            held.Contains(WellSitePermissions.AssetViewsEdit) && !view.HasUnavailableMembers,
            held.Contains(WellSitePermissions.AssetViewsDelete));
}
