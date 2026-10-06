namespace WellSiteAutoPilot.Messaging.Contracts.Execution;

public sealed record ExecutionRequestedV1(
    Guid ExecutionId,
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    string Mode,
    DateTimeOffset RequestedAtUtc);

public sealed record ExecutionCompletedV1(
    Guid ExecutionId,
    DateTimeOffset CompletedAtUtc,
    string ResultCode);

public sealed record ExecutionFailedV1(
    Guid ExecutionId,
    DateTimeOffset FailedAtUtc,
    string FailureCode,
    bool RequiresAttention);
