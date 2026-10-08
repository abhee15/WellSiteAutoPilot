namespace WellSiteAutoPilot.Domain.Executions;

public sealed record ExecutionRecord(
    Guid Id,
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid? AssetId,
    string? AssetExternalId,
    string? Quantity,
    ExecutionMode Mode,
    ExecutionStatus Status,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc,
    int RequestContractVersion = 1,
    ExecutionTriggerKind Trigger = ExecutionTriggerKind.Manual,
    DateTimeOffset? ScheduledForUtc = null,
    string? RequestPayloadJson = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? ResultCode = null,
    string? FailureCode = null,
    string? OutputJson = null);
