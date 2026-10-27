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
    ExecutionTriggerKind Trigger,
    DateTimeOffset? ScheduledForUtc,
    ExecutionMode Mode,
    ExecutionStatus Status,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultCode,
    string? FailureCode,
    string? OutputJson,
    ExecutionProvenanceResponse? Provenance = null);

public sealed record RequestConfiguredShadowRequest(Guid RequestId);

public sealed record ConfiguredShadowReceiptResponse(Guid ExecutionId, Guid LogicInstanceId,
    Guid ConfigurationRevisionId, int RequestContractVersion, ExecutionMode Mode, ExecutionTriggerKind Trigger);

public sealed record ExecutionProvenanceResponse(string PackageSha256,
    IReadOnlyCollection<ExecutionConnectionResponse> Connections, bool ConnectionsTruncated);
public sealed record ExecutionConnectionResponse(Guid ConnectionId, int Revision, int AssetCount);
