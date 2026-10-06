using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Executions;

public interface IExecutionRepository
{
    Task AddRequestedAsync(
        ExecutionRecord execution,
        CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}
