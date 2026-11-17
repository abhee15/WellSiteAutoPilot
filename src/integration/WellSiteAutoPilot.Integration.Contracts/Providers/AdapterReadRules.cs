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
    IReadOnlyList<StructuredCurrentRead>? StructuredCurrentReads = null)
{
    /// <summary>
    /// Authoring only. A V2 contract declares a scalar current reading as the registry's own <c>engineering.current</c>
    /// capability with a number schema, while <see cref="AllowsCurrentRead"/> keeps matching scalar reads only when both are
    /// absent. This deliberately does not widen <see cref="AllowsCurrentRead"/>: assignment must keep rejecting inputs the
    /// worker cannot execute yet, so a draft can be checked against a sample Asset without implying it is runnable.
    /// </summary>
    public bool AllowsScalarCurrentDeclaration(string? quantity, string? requiredCapability, string? schemaType) =>
        quantity is not null && requiredCapability == AdapterReadRules.EngineeringCurrentCapability &&
        schemaType == "number" && CurrentQuantities.Contains(quantity);

    /// <summary>True when a current read of this quantity, with this capability and schema, is one the adapter serves.</summary>
    public bool AllowsCurrentRead(string? quantity, string? requiredCapability, string? schemaId) =>
        quantity is not null && (
            requiredCapability is null && schemaId is null && CurrentQuantities.Contains(quantity) ||
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
            [new StructuredCurrentRead(ForeSiteSurfaceCard.Quantity, StructuredCurrentCapability, ForeSiteSurfaceCard.SchemaId)]),
        new(CygNetKey, CygNetResourceKind, CygNetQuantities)
    ];

    public static AdapterReadRule? Find(string? adapterKey) =>
        Rules.FirstOrDefault(rule => string.Equals(rule.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase));
}
