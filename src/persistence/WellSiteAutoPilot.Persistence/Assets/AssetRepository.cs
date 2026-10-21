using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository(
    WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IAssetRepository
{
    public Task<bool> AssetTypeKeyIsHiddenAsync(string key, CancellationToken cancellationToken = default) =>
        dbContext.AssetTypes.AnyAsync(item => item.Key == key && !item.IsActive, cancellationToken);

    public Task<bool> AssetTypeKeyExistsAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        dbContext.AssetTypes.AnyAsync(
            item => item.Key == key,
            cancellationToken);

    public async Task<AssetTypeDefinition?> GetAssetTypeAsync(
        Guid assetTypeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.AssetTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == assetTypeId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAsync(
        CancellationToken cancellationToken = default) =>
        (await dbContext.AssetTypes
            .AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .ToArrayAsync(cancellationToken))
        .Select(ToDomain)
        .ToArray();

    public async Task AddAssetTypeAsync(
        AssetTypeDefinition assetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetType);

        StageAssetType(assetType);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void StageAssetType(AssetTypeDefinition assetType)
    {
        dbContext.AssetTypes.Add(new AssetTypeEntity
        {
            Id = assetType.Id,
            Key = assetType.Key,
            DisplayName = assetType.DisplayName,
            SchemaVersion = assetType.SchemaVersion,
            AttributeSchemaJson = assetType.AttributeSchemaJson,
            IsActive = assetType.IsActive,
            CreatedAtUtc = assetType.CreatedAtUtc
        });
    }

    public async Task<Asset?> GetAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    // These public entry points intentionally expose distinct authorization
    // semantics. Shared mechanics below guarantee filter/order/page parity.
    public Task<IReadOnlyCollection<Asset>> ListAssetsAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(
            dbContext.Assets.AsNoTracking(),
            assetTypeId,
            parentAssetId,
            limit,
            cancellationToken);

    public Task<IReadOnlyCollection<Asset>> ListAssetsInScopeAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            // Empty scope means no authorization, never an unrestricted query.
            return Task.FromResult<IReadOnlyCollection<Asset>>([]);
        }

        var ids = allowedAssetIds.Distinct().ToArray();
        return ListCoreAsync(
            dbContext.Assets.AsNoTracking().Where(item => ids.Contains(item.Id)),
            assetTypeId,
            parentAssetId,
            limit,
            cancellationToken);
    }

    private static async Task<IReadOnlyCollection<Asset>> ListCoreAsync(
        IQueryable<AssetEntity> baseQuery,
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        CancellationToken cancellationToken)
    {
        // The scoped query starts with its mandatory SQL predicate before
        // any type/parent filtering or Take(limit).
        if (assetTypeId is { } typeId)
        {
            baseQuery = baseQuery.Where(item => item.AssetTypeId == typeId);
        }

        if (parentAssetId is { } parentId)
        {
            baseQuery = baseQuery.Where(item => item.ParentAssetId == parentId);
        }

        return (await baseQuery
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlySet<Guid>> GetActiveAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count == 0) return new HashSet<Guid>();
        var ids = assetIds.Distinct().ToArray();
        return (await dbContext.Assets.AsNoTracking()
            .Where(item => ids.Contains(item.Id) && item.IsActive)
            .Select(item => item.Id).ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    public async Task<IReadOnlyCollection<Asset>> ListAssetDirectoryPageAsync(
        int offset, int limit,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Assets.AsNoTracking()
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip(offset).Take(limit)
            .ToArrayAsync(cancellationToken))
        .Select(ToDomain).ToArray();

    public async Task AddAssetAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        StageAsset(asset);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void StageAsset(Asset asset)
    {
        dbContext.Assets.Add(new AssetEntity
        {
            Id = asset.Id,
            AssetTypeId = asset.AssetTypeId,
            Name = asset.Name,
            ParentAssetId = asset.ParentAssetId,
            AttributeValuesJson = asset.AttributeValuesJson,
            IsActive = asset.IsActive,
            CreatedAtUtc = asset.CreatedAtUtc,
            OwnerConnectionId = asset.OwnerConnectionId
        });
    }

    private static AssetTypeDefinition ToDomain(AssetTypeEntity entity) => new(
        entity.Id,
        entity.Key,
        entity.DisplayName,
        entity.SchemaVersion,
        entity.AttributeSchemaJson,
        entity.IsActive,
        entity.CreatedAtUtc,
        entity.HiddenNote,
        entity.HiddenAtUtc,
        entity.HiddenBy);

    private static Asset ToDomain(AssetEntity entity) => new(
        entity.Id,
        entity.AssetTypeId,
        entity.Name,
        entity.ParentAssetId,
        entity.AttributeValuesJson,
        entity.IsActive,
        entity.CreatedAtUtc,
        entity.OwnerConnectionId);
}
