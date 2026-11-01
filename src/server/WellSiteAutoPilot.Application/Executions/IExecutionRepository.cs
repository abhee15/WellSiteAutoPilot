using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Executions;

public interface IExecutionRepository
{
    Task<ManualShadowRequestReceipt> RequestManualShadowAuthorizedAsync(Guid configurationId, Guid revisionId,
        Guid requestId, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists authorized executions, newest first. Authorization scope is applied before the optional
    /// configured Logic and Asset filters and before the limit, so a filter can never reveal or
    /// displace anything outside the caller's scope.
    /// </summary>
    Task<IReadOnlyCollection<ExecutionRecord>> ListAuthorizedAsync(AccessSubject subject, ExecutionStatus? status, int limit,
        Guid? configuredLogicId = null, Guid? assetId = null, CancellationToken cancellationToken = default);

    Task AddRequestedAsync(
        ExecutionRecord execution,
        CancellationToken cancellationToken = default);

    Task<ExecutionRecord> AddConfiguredRequestedAsync(
        ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command,
        CancellationToken cancellationToken = default) =>
        AddConfiguredRequestedAsync(execution, command, null, cancellationToken);

    /// <summary>Scheduled dispatch passes the configured read authority; null means no live read may be pinned.</summary>
    Task<ExecutionRecord> AddConfiguredRequestedAsync(
        ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command,
        WellSiteAutoPilot.Application.Security.AccessSubject? readSubject,
        CancellationToken cancellationToken = default);

    Task<ExecutionRecord> AddManualConfiguredRequestedAsync(ExecutionRecord execution,
        ConfiguredShadowExecutionCommand command, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task ApplyStartedAsync(Guid messageId, string consumer, Guid executionId,
        DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetInScopeAsync(
        Guid executionId,
        IReadOnlyCollection<Guid> allowedAssetIds,
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

    Task<ExecutionResultDisposition> ApplyCompletedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string resultCode,
        string? outputJson,
        CancellationToken cancellationToken = default);

    Task<ExecutionResultDisposition> ApplyFailedAsync(
        Guid messageId,
        string consumer,
        Guid executionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken = default);

    Task<ExecutionResultDisposition> ApplyFailedWithDiagnosticsAsync(Guid messageId, string consumer,
        Guid executionId, DateTimeOffset startedAtUtc, DateTimeOffset failedAtUtc, string failureCode,
        string? diagnosticsJson, CancellationToken cancellationToken = default) =>
        ApplyFailedAsync(messageId, consumer, executionId, startedAtUtc, failedAtUtc, failureCode, cancellationToken);
}
