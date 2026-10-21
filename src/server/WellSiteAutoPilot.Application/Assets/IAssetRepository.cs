using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Application.Assets;

public interface IAssetRepository
{
    Task<AssetTypeDefinition?> GetAssetTypeAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAuthorizedAsync(AccessSubject subject,
        CancellationToken cancellationToken = default);

    Task AddAssetTypeAuthorizedAsync(AssetTypeDefinition assetType, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    /// <summary>Hides (with an optional note) or restores a type; hiding keeps existing Assets working.</summary>
    Task SetAssetTypeHiddenAuthorizedAsync(Guid assetTypeId, bool hidden, string? note, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>Deletes a type that no Asset has ever used.</summary>
    Task DeleteAssetTypeAuthorizedAsync(Guid assetTypeId, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task<bool> AssetTypeKeyIsHiddenAsync(string key, CancellationToken cancellationToken = default);

    Task AddAssetAuthorizedAsync(Asset asset, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Asset?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Asset>> ListAuthorizedAsync(AccessSubject subject, Guid? assetTypeId, Guid? parentAssetId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorized assets directly below <paramref name="parentAssetId"/>, or the top level when it is
    /// null: assets with no parent, or whose parent this subject may not see.
    /// </summary>
    Task<IReadOnlyCollection<AssetTreeNode>> ListChildrenAuthorizedAsync(AccessSubject subject, Guid? parentAssetId,
        int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>The top level narrowed to one owner (a Connection, or the standalone Assets).</summary>
    Task<IReadOnlyCollection<AssetTreeNode>> ListChildrenAuthorizedAsync(AccessSubject subject, Guid? parentAssetId,
        AssetOwnerScope? owner, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>The Connections (and the standalone group) that own at least one Asset this subject may see.</summary>
    Task<IReadOnlyCollection<AssetOwnerSummary>> ListOwnersAuthorizedAsync(AccessSubject subject,
        CancellationToken cancellationToken = default);

    /// <summary>Authorized assets whose name contains <paramref name="text"/>, each with its visible location.</summary>
    Task<IReadOnlyCollection<AssetSearchHit>> SearchAuthorizedAsync(AccessSubject subject, string text,
        Guid? assetTypeId, int offset, int limit, CancellationToken cancellationToken = default);

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

    Task<IReadOnlySet<Guid>> GetActiveAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stable administrator directory paging; never reuse the resource-scoped
    /// listing for grant assignment across the full deployment.
    /// </summary>
    Task<IReadOnlyCollection<Asset>> ListAssetDirectoryPageAsync(
        int offset, int limit,
        CancellationToken cancellationToken = default);

    Task AddAssetAsync(
        Asset asset,
        CancellationToken cancellationToken = default);
}
