using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Assets;

internal static class AssetViewMapping
{
    public const string PersonalName = "personal";
    public const string SharedName = "shared";

    public static AssetViewDefinition ToDefinition(AssetViewDefinitionDto? dto) => dto is null
        ? throw new ArgumentNullException(nameof(dto))
        : new AssetViewDefinition(dto.Version, dto.Connections, dto.AssetTypeIds, dto.Activity, dto.Search, dto.Grouping);

    /// <summary>Null means "not given"; anything but personal or shared is refused.</summary>
    public static AssetViewVisibility? ToVisibility(string? value) => value switch
    {
        null => null,
        PersonalName => AssetViewVisibility.Personal,
        SharedName => AssetViewVisibility.Shared,
        _ => throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
            "Visibility must be personal or shared.")
    };

    /// <summary>
    /// The View as the viewer may see it. A non-owner's definition keeps only the Connections that viewer can see (and
    /// Standalone), so a shared View never reveals which other Connections it names.
    /// </summary>
    public static AssetViewResponse ToResponse(AssetViewRecord view, Guid viewerUserId, bool viewerMayShare,
        IReadOnlySet<Guid> visibleConnections)
    {
        var isOwner = view.OwnerUserId == viewerUserId;
        var connections = view.Definition.Connections;
        if (!isOwner)
            connections = connections.Where(item => item == AssetViewDefinitionRules.Standalone ||
                (Guid.TryParse(item, out var id) && visibleConnections.Contains(id))).ToArray();
        return new AssetViewResponse(view.Id, view.Name,
            new AssetViewDefinitionDto(view.Definition.Version, connections, view.Definition.AssetTypeIds,
                view.Definition.Activity, view.Definition.Search, view.Definition.Grouping),
            view.Revision, view.CreatedAtUtc, view.UpdatedAtUtc,
            view.Visibility == AssetViewVisibility.Shared ? SharedName : PersonalName, view.OwnerName, isOwner,
            isOwner && (view.Visibility == AssetViewVisibility.Personal || viewerMayShare),
            view.Definition.Connections.Count - connections.Count);
    }
}
