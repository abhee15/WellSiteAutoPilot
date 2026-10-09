using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Domain.Assets;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed class AssetRepository(
    WellSiteAutoPilotDbContext dbContext) : IAssetRepository
{
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

        await dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task<IReadOnlyCollection<Asset>> ListAssetsAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Assets.AsNoTracking();

        if (assetTypeId is not null)
        {
            query = query.Where(item => item.AssetTypeId == assetTypeId.Value);
        }

        if (parentAssetId is not null)
        {
            query = query.Where(item => item.ParentAssetId == parentAssetId.Value);
        }

        return (await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<Asset>> ListAssetsInScopeAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            return [];
        }

        var ids = allowedAssetIds.Distinct().ToArray();
        var query = dbContext.Assets
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id));

        if (assetTypeId is not null)
        {
            query = query.Where(item => item.AssetTypeId == assetTypeId.Value);
        }

        if (parentAssetId is not null)
        {
            query = query.Where(item => item.ParentAssetId == parentAssetId.Value);
        }

        return (await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task AddAssetAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        dbContext.Assets.Add(new AssetEntity
        {
            Id = asset.Id,
            AssetTypeId = asset.AssetTypeId,
            Name = asset.Name,
            ParentAssetId = asset.ParentAssetId,
            AttributeValuesJson = asset.AttributeValuesJson,
            IsActive = asset.IsActive,
            CreatedAtUtc = asset.CreatedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AssetTypeDefinition ToDomain(AssetTypeEntity entity) => new(
        entity.Id,
        entity.Key,
        entity.DisplayName,
        entity.SchemaVersion,
        entity.AttributeSchemaJson,
        entity.IsActive,
        entity.CreatedAtUtc);

    private static Asset ToDomain(AssetEntity entity) => new(
        entity.Id,
        entity.AssetTypeId,
        entity.Name,
        entity.ParentAssetId,
        entity.AttributeValuesJson,
        entity.IsActive,
        entity.CreatedAtUtc);
}
