using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository
{
    public async Task<AssetTypeDefinition?> GetAssetTypeAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.AssetTypes(dbContext, subject, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(type => type.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAuthorizedAsync(AccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return (await ScopedAccessQueries.AssetTypes(dbContext, subject, (clock ?? TimeProvider.System).GetUtcNow())
            .OrderBy(type => type.DisplayName).ThenBy(type => type.Id).ToArrayAsync(cancellationToken))
            .Select(ToDomain).ToArray();
    }

    public async Task<Asset?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var entity = await ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(asset => asset.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public Task<IReadOnlyCollection<Asset>> ListAuthorizedAsync(AccessSubject subject,
        Guid? assetTypeId, Guid? parentAssetId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        return ListCoreAsync(ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow()), assetTypeId, parentAssetId, limit, cancellationToken);
    }
}
