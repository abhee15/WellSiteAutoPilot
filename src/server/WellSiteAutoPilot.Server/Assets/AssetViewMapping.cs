using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Application.Assets.Views;

namespace WellSiteAutoPilot.Server.Assets;

internal static class AssetViewMapping
{
    public static AssetViewDefinition ToDefinition(AssetViewDefinitionDto? dto) => dto is null
        ? throw new ArgumentNullException(nameof(dto))
        : new AssetViewDefinition(dto.Version, dto.Connections, dto.AssetTypeIds, dto.Activity, dto.Search, dto.Grouping);

    public static AssetViewResponse ToResponse(AssetViewRecord view) => new(view.Id, view.Name,
        new AssetViewDefinitionDto(view.Definition.Version, view.Definition.Connections, view.Definition.AssetTypeIds,
            view.Definition.Activity, view.Definition.Search, view.Definition.Grouping),
        view.Revision, view.CreatedAtUtc, view.UpdatedAtUtc);
}
