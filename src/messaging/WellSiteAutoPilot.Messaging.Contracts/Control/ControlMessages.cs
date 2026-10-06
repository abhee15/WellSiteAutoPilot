namespace WellSiteAutoPilot.Messaging.Contracts.Control;

public sealed record ControlActionRequestedV1(
    Guid ControlActionId,
    Guid ExecutionId,
    Guid AssetId,
    string ControlDomain,
    string Command,
    decimal RequestedValue,
    string Unit,
    DateTimeOffset RequestedAtUtc);

public sealed record ControlActionCompletedV1(
    Guid ControlActionId,
    Guid ExecutionId,
    DateTimeOffset CompletedAtUtc,
    decimal? VerifiedValue,
    string? Unit);

public sealed record ControlActionFailedV1(
    Guid ControlActionId,
    Guid ExecutionId,
    DateTimeOffset FailedAtUtc,
    string FailureCode,
    bool RequiresAttention);
