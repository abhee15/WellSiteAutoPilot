namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Versioned metadata only. Execution remains on typed provider interfaces in the Gateway.</summary>
public sealed record AdapterOperationDescriptor(
    string Key,
    string DisplayName,
    string Description,
    ProviderCapabilityKind Capability,
    string CapabilityId,
    string ContractVersion,
    AdapterOperationSchema Request,
    AdapterOperationSchema Response,
    string TimestampSemantics,
    string QualitySemantics,
    AdapterOperationLimits Limits,
    string? Quantity = null,
    string? ResourceKind = null,
    string? Unit = null,
    string? StructuredSchemaId = null,
    AdapterOperationSchema? StructuredResponse = null);

public sealed record AdapterOperationSchema(string Id, IReadOnlyList<AdapterOperationField> Fields, string DataType = "object");
public sealed record AdapterOperationField(string Name, string DataType, bool Required, string Description);
public sealed record AdapterOperationLimits(int MaximumQuantities, int MaximumValueBytes, int MaximumReplyBytes,
    int? TimeoutSeconds = null, int? MaximumWindowDays = null, int? MaximumSamples = null);

/// <summary>Shared schemas for the actual engineering-value transport; adapters supply source semantics and limits.</summary>
public static class EngineeringOperationSchemas
{
    public static readonly AdapterOperationSchema CurrentRequest = new("engineering.current-request/v1", [
        new("externalId", "string", true, "Imported object identity belonging to the selected connection."),
        new("quantity", "string", true, "The exact logical quantity declared by this operation.")]);
    public static readonly AdapterOperationSchema CurrentResponse = new("engineering.value/v1", [
        new("quantity", "string", true, "Logical quantity."),
        new("value", "decimal|null", false, "Scalar value; mutually exclusive with structuredValue."),
        new("structuredValue", "object|null", false, "Schema identifier and bounded structured payload."),
        new("unit", "string", true, "Engineering unit returned by the source or configured mapping."),
        new("timestampUtc", "date-time", true, "Source timestamp, normalized to UTC."),
        new("quality", "string", true, "Source quality; unknown quality is never promoted to Good."),
        new("source", "string", true, "Provenance; simulated sources are identified explicitly."),
        new("retrievedAtUtc", "date-time|null", false, "Retrieval time, distinct from source time."),
        new("timestampKind", "string|null", false, "Meaning of the acquisition timestamp.")]);

    public static AdapterOperationDescriptor Current(string key, string name, string description, string quantity,
        string resourceKind, string? unit, string timestamp, string quality, int timeoutSeconds, string? structuredSchemaId = null) =>
        new(key, name, description, ProviderCapabilityKind.CurrentData,
            structuredSchemaId is null ? "engineering.current" : "structured.current", "1.0",
            CurrentRequest, CurrentResponse, timestamp, quality,
            new(1, MeasurementPayloadLimits.MaximumValueBytes, MeasurementPayloadLimits.MaximumReplyBytes, timeoutSeconds),
            quantity, resourceKind, unit, structuredSchemaId);
}
