using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Executions;

public interface IExecutionRepository
{
    Task AddRequestedAsync(
        ExecutionRecord execution,
        CancellationToken cancellationToken = default);

    Task AddConfiguredRequestedAsync(
        ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command,
        CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ExecutionRecord>> ListAsync(
        ExecutionStatus? status,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ExecutionRecord>> ListInScopeAsync(
        ExecutionStatus? status,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetAssetScopeAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    Task<bool> ScheduledOccurrenceExistsAsync(
        Guid configurationRevisionId,
        DateTimeOffset scheduledForUtc,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveAssetOverlapAsync(
        Guid logicInstanceId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyCompletedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string resultCode,
        string? outputJson,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyFailedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken = default);
}
