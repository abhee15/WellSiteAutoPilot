using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>Bounded, authorized reads for asset details and their configured logic.</summary>
public interface IAssetDetailReader
{
    /// <summary>Null when the asset is not visible; grouping is explicit, never inferred from children.</summary>
    Task<bool?> IsGroupingNodeAuthorizedAsync(AccessSubject subject, Guid assetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfiguredLogicDefinition>> ListLogicAuthorizedAsync(AccessSubject subject, Guid assetId,
        int offset, int limit, CancellationToken cancellationToken);
}
