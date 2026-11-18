namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// What an adapter lets the Server read directly from one imported source object: the resource kind its discovery
/// reports, the scalar current quantities it can serve and any structured current reads. The Server asks here instead
/// of knowing each adapter.
/// </summary>
public sealed record AdapterReadRule(
    string AdapterKey,
    string ResourceKind,
    IReadOnlySet<string> CurrentQuantities,
    IReadOnlyList<StructuredCurrentRead>? StructuredCurrentReads = null,
    bool ServesDailyQuantities = false)
{
    /// <summary>
    /// A V2 contract declares a scalar current reading as the registry's own <c>engineering.current</c> capability with a
    /// number schema. Authoring uses this type-aware check; <see cref="AllowsCurrentRead"/> (which has only the schema
    /// identity) accepts the same declaration, so the caller that holds the schema must also require a numeric root.
    /// </summary>
    public bool AllowsScalarCurrentDeclaration(string? quantity, string? requiredCapability, string? schemaType) =>
        quantity is not null && schemaType == "number" && (
            requiredCapability == AdapterReadRules.EngineeringCurrentCapability && CurrentQuantities.Contains(quantity) ||
            ServesDaily(quantity, requiredCapability));

    /// <summary>
    /// True when this adapter serves the provider's own daily field (any identifier-shaped name from its daily record)
    /// as "the latest completed production day". The field list is the provider's, discovered per well, not registered here.
    /// </summary>
    public bool ServesDaily(string? quantity, string? requiredCapability) =>
        ServesDailyQuantities && requiredCapability == AdapterReadRules.EngineeringDailyCapability &&
        ForeSiteDailyAverageContract.IsObservableQuantityName(quantity);

    /// <summary>True when a current read of this quantity, with this capability and schema, is one the adapter serves.</summary>
    public bool AllowsCurrentRead(string? quantity, string? requiredCapability, string? schemaId) =>
        quantity is not null && (
            requiredCapability is null && schemaId is null && CurrentQuantities.Contains(quantity) ||
            // A V2 scalar: the registry's own capability with the module's own schema identity. Callers that know the
            // declared schema must also require a numeric root type (see AllowsScalarCurrentDeclaration).
            requiredCapability == AdapterReadRules.EngineeringCurrentCapability && schemaId is not null &&
                CurrentQuantities.Contains(quantity) ||
            schemaId is not null && ServesDaily(quantity, requiredCapability) ||
            StructuredCurrentReads?.Any(read => read.Quantity == quantity && read.RequiredCapability == requiredCapability &&
                                                read.SchemaId == schemaId) == true);
}

/// <summary>A structured (non-scalar) current read an adapter serves: the quantity, the capability it needs and its schema.</summary>
public sealed record StructuredCurrentRead(string Quantity, string RequiredCapability, string SchemaId);

/// <summary>The adapters that can serve direct current reads for saved connections.</summary>
public static class AdapterReadRules
{
    public const string StructuredCurrentCapability = "structured.current";
    public const string EngineeringCurrentCapability = "engineering.current";

    /// <summary>A number from the provider's latest completed production-day record (for example yesterday's total).</summary>
    public const string EngineeringDailyCapability = "engineering.daily";

    /// <summary>True for the capabilities that read one scalar number.</summary>
    public static bool IsScalarCapability(string? capability) =>
        capability is EngineeringCurrentCapability or EngineeringDailyCapability;
    public const string ForeSiteKey = "foresite";
    public const string CygNetKey = "cygnet";
    public const string CygNetResourceKind = "cygnet.facility";

    /// <summary>Quantities CygNet connections can serve. Which point (UDC) backs each is the Gateway's decision.</summary>
    public static readonly IReadOnlySet<string> CygNetQuantities = new HashSet<string>(StringComparer.Ordinal)
    {
        "PumpFillage", "CasingPressure", "TubingPressure", "MotorAmps", "RodLoad"
    };

    private static readonly AdapterReadRule[] Rules =
    [
        new(ForeSiteKey, "foresite.well", new HashSet<string>(StringComparer.Ordinal) { "PumpFillage", "AveragePumpFillage" },
            [new StructuredCurrentRead(ForeSiteSurfaceCard.Quantity, StructuredCurrentCapability, ForeSiteSurfaceCard.SchemaId)],
            ServesDailyQuantities: true),
        new(CygNetKey, CygNetResourceKind, CygNetQuantities)
    ];

    public static AdapterReadRule? Find(string? adapterKey) =>
        Rules.FirstOrDefault(rule => string.Equals(rule.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase));
}
