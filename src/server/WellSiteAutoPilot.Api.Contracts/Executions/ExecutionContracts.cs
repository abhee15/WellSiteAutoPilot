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
    Guid AssetId,
    string AssetExternalId,
    string Quantity,
    string Mode,
    string Status,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultCode,
    string? FailureCode);
