using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository
{
    public async Task<AssetConnectionSource?> GetConnectionSourceAuthorizedAsync(AccessSubject subject, Guid assetId, Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        // The paired AssetsRead grant, owner and explicit identity are applied in SQL before any binding is read.
        var source = await VisibleAssets(subject).Where(asset => asset.Id == assetId && asset.OwnerConnectionId == connectionId)
            .Select(asset => new
            {
                asset.Id, asset.IsActive,
                Bindings = dbContext.SourceBindings.AsNoTracking()
                    .Where(binding => binding.AssetId == asset.Id && binding.ConnectionId == connectionId)
                    .OrderBy(binding => binding.Id).Take(2)
                    .Select(binding => new AssetConnectionBinding(binding.ResourceKind, binding.ExternalId)).ToArray()
            }).SingleOrDefaultAsync(cancellationToken);
        return source is null ? null : new(source.Id, connectionId, source.IsActive, source.Bindings);
    }

    public async Task<AssetSourceInfo?> GetSourceAuthorizedAsync(AccessSubject subject, Guid assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!await VisibleAssets(subject).AnyAsync(item => item.Id == assetId, cancellationToken)) return null;
        var binding = await dbContext.SourceBindings.AsNoTracking().Where(item => item.AssetId == assetId)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new { item.ConnectionId, item.ResourceKind, item.ExternalId, item.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        if (binding is null) return null;
        var name = await dbContext.Connections.AsNoTracking().Where(item => item.Id == binding.ConnectionId)
            .Select(item => item.Name).FirstOrDefaultAsync(cancellationToken);
        return new AssetSourceInfo(binding.ConnectionId, name, binding.ResourceKind, binding.ExternalId, binding.CreatedAtUtc);
    }
}
