namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterOperationsResponse(string AdapterKey, string DisplayName, string Description,
    IReadOnlyList<AdapterOperationResponse> Operations, IReadOnlyList<AdapterCapabilityGroupResponse> Capabilities);
public sealed record AdapterOperationFieldResponse(string Name, string DataType, bool Required, string Description);
public sealed record AdapterOperationSchemaResponse(string Id, IReadOnlyList<AdapterOperationFieldResponse> Fields, string DataType);
public sealed record AdapterOperationLimitsResponse(int MaximumQuantities, int MaximumValueBytes, int MaximumReplyBytes,
    int? TimeoutSeconds, int? MaximumWindowDays, int? MaximumSamples, int? MaximumSourceBytes);
public sealed record AdapterOperationResponse(string Key, string DisplayName, string Description, string Capability,
    string CapabilityId, string ContractVersion, string Effect, AdapterOperationSchemaResponse Request,
    AdapterOperationSchemaResponse Response, string TimestampSemantics, string QualitySemantics,
    AdapterOperationLimitsResponse Limits, string? Quantity, string? ResourceKind, string? Unit, string? StructuredSchemaId, AdapterOperationSchemaResponse? StructuredResponse, AdapterOperationDocumentationResponse? Documentation);

/// <summary>Connection readiness only. It does not authorize asset reads or assert that an individual point exists.</summary>
public sealed record ConnectionOperationsResponse(Guid ConnectionId, int ConnectionRevision, string AdapterKey,
    string State, string Reason, IReadOnlyList<string> OperationKeys);

/// <summary>Metadata eligibility only. This response authorizes no execution and contains no source IDs/settings/values.</summary>
public sealed record AssetOperationsResponse(Guid ConnectionId, int ConnectionRevision, Guid AssetId, string AdapterKey,
    string ConnectionState, IReadOnlyList<AssetOperationEligibilityResponse> Operations);
public sealed record AssetOperationEligibilityResponse(string Key, string ContractVersion, AssetOperationEligibilityState State, string Reason);

public sealed record AdapterWorkflowStepResponse(string Key, string Description, string? ProviderApi,
    string? Requirement, string? OnFailure);
public sealed record AdapterWorkflowGuideResponse(string Key, string DisplayName, string Purpose, string Readiness,
    IReadOnlyList<string> OperationKeys, IReadOnlyList<AdapterWorkflowStepResponse> Steps, string ContractVersion);
public sealed record AdapterCapabilityGroupResponse(string Key, string DisplayName, string Description,
    string ContractVersion, IReadOnlyList<string> OperationKeys,
    IReadOnlyList<AdapterWorkflowGuideResponse>? Workflows = null);
public sealed record AdapterOperationDocumentationResponse(string Invocation, string ExecutionBoundary,
    string RequestExampleJson, string ResponseExampleJson, string? ProviderReference = null);
