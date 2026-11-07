namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterOperationsResponse(string AdapterKey, string DisplayName, string Description,
    IReadOnlyList<AdapterOperationResponse> Operations);
public sealed record AdapterOperationFieldResponse(string Name, string DataType, bool Required, string Description);
public sealed record AdapterOperationSchemaResponse(string Id, IReadOnlyList<AdapterOperationFieldResponse> Fields, string DataType);
public sealed record AdapterOperationLimitsResponse(int MaximumQuantities, int MaximumValueBytes, int MaximumReplyBytes,
    int? TimeoutSeconds, int? MaximumWindowDays, int? MaximumSamples);
public sealed record AdapterOperationResponse(string Key, string DisplayName, string Description, string Capability,
    string CapabilityId, string ContractVersion, string Effect, AdapterOperationSchemaResponse Request,
    AdapterOperationSchemaResponse Response, string TimestampSemantics, string QualitySemantics,
    AdapterOperationLimitsResponse Limits, string? Quantity, string? ResourceKind, string? Unit, string? StructuredSchemaId, AdapterOperationSchemaResponse? StructuredResponse);

/// <summary>Connection readiness only. It does not authorize asset reads or assert that an individual point exists.</summary>
public sealed record ConnectionOperationsResponse(Guid ConnectionId, int ConnectionRevision, string AdapterKey,
    string State, string Reason, IReadOnlyList<string> OperationKeys);
