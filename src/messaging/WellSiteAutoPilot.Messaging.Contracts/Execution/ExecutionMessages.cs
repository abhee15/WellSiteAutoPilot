namespace WellSiteAutoPilot.Messaging.Contracts.Execution;

public sealed record ExecutionRequestedV1(
    Guid ExecutionId,
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid AssetId,
    string AssetExternalId,
    string Quantity,
    string Mode,
    DateTimeOffset RequestedAtUtc);

public sealed record ExecutionCompletedV1(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ResultCode,
    string? OutputJson);

public sealed record ExecutionFailedV1(
    Guid ExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FailedAtUtc,
    string FailureCode,
    bool RequiresAttention);
