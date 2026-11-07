using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>Private, bounded source metadata for one authorized Asset and its exact owning Connection.</summary>
public sealed record AssetConnectionSource(Guid AssetId, Guid ConnectionId, bool IsActive, IReadOnlyList<AssetConnectionBinding> Bindings);
public sealed record AssetConnectionBinding(string ResourceKind, string ExternalId);

public interface IAssetConnectionSourceReader
{
    /// <summary>Null for missing, unauthorized or differently owned Assets. At most two bindings distinguish ambiguity.</summary>
    Task<AssetConnectionSource?> GetConnectionSourceAuthorizedAsync(AccessSubject subject, Guid assetId, Guid connectionId,
        CancellationToken cancellationToken = default);
}
