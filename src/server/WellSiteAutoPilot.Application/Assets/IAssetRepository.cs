using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Application.Assets;

public interface IAssetRepository
{
    Task<bool> AssetTypeKeyExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<AssetTypeDefinition?> GetAssetTypeAsync(
        Guid assetTypeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAsync(
        CancellationToken cancellationToken = default);

    Task AddAssetTypeAsync(
        AssetTypeDefinition assetType,
        CancellationToken cancellationToken = default);

    Task<Asset?> GetAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Asset>> ListAssetsAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Asset>> ListAssetsInScopeAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default);

    Task AddAssetAsync(
        Asset asset,
        CancellationToken cancellationToken = default);
}
