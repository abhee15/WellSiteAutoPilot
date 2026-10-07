namespace WellSiteAutoPilot.Api.Contracts.Executions;

public sealed record RequestShadowExecutionRequest(
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid AssetId,
    string AssetExternalId,
    string Quantity);

public sealed record ExecutionResponse(
    Guid ExecutionId,
    Guid LogicInstanceId,
    string ModuleId,
    string ModuleVersion,
    Guid ConfigurationRevisionId,
    Guid? AssetId,
    string? AssetExternalId,
    string? Quantity,
    int RequestContractVersion,
    string Trigger,
    DateTimeOffset? ScheduledForUtc,
    string Mode,
    string Status,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultCode,
    string? FailureCode,
    string? OutputJson);
