using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Executions;

public interface IExecutionHistoryReader
{
    /// <summary>Authorized keyset history for a configuration, optionally narrowed to a pinned Asset scope.</summary>
    Task<IReadOnlyList<ExecutionRecord>> ListHistoryAuthorizedAsync(AccessSubject subject, Guid? assetId,
        Guid configuredLogicId, Guid? after, int limit, CancellationToken cancellationToken);
}
