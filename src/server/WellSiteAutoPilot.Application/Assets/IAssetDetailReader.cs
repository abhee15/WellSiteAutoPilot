using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>Bounded, authorized reads for asset details and their run history.</summary>
public interface IAssetDetailReader
{
    /// <summary>Null when the asset is not visible; grouping is explicit, never inferred from children.</summary>
    Task<bool?> IsGroupingNodeAuthorizedAsync(AccessSubject subject, Guid assetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfiguredLogicDefinition>> ListLogicAuthorizedAsync(AccessSubject subject, Guid assetId,
        int offset, int limit, CancellationToken cancellationToken);

    /// <summary>A stable keyset page, including pinned execution asset scopes rather than current View membership.</summary>
    Task<IReadOnlyList<ExecutionRecord>> ListHistoryAuthorizedAsync(AccessSubject subject, Guid assetId,
        Guid configuredLogicId, Guid? after, int limit, CancellationToken cancellationToken);
}
