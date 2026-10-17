namespace WellSiteAutoPilot.Api.Contracts.Executions;

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

public sealed record RequestConfiguredShadowRequest(Guid RequestId);

public sealed record ConfiguredShadowReceiptResponse(Guid ExecutionId, Guid LogicInstanceId,
    Guid ConfigurationRevisionId, int RequestContractVersion, string Mode, string Trigger);
