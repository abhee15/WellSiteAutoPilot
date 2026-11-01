namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public static class AdapterValueKinds
{
    public const string Number = "Number";
    public const string Structured = "Structured";
}

/// <summary>
/// What one quantity an adapter can serve looks like, so authors can pick it without knowing the provider.
/// <paramref name="Unit"/> is the unit the adapter returns by default (a connection's point mapping may change it);
/// <paramref name="SchemaId"/> and <paramref name="RequiredCapability"/> are set for structured values only.
/// <paramref name="TimestampMeaning"/> says, in a sentence, what the value's time stamp records.
/// <paramref name="NeedsMapping"/> is true when connections must map the quantity before it can be read.
/// </summary>
public sealed record AdapterQuantity(
    string Name,
    string Label,
    string ValueKind,
    string? Unit,
    string TimestampMeaning,
    string? SchemaId = null,
    string? RequiredCapability = null,
    bool NeedsMapping = false);

/// <summary>
/// Self-description of the quantities each adapter can read. It complements <see cref="AdapterReadRules"/>, which
/// stays the authority on what the Server may read; every quantity listed there is described here.
/// </summary>
public static class AdapterQuantities
{
    public const string ForeSiteSurfaceCards = "SurfaceCards";
    public const string ForeSiteSurfaceCardsSchemaId = "foresite.surface-cards/v1";
    public const string StructuredCurrentCapability = "structured.current";

    private const string ControllerScan =
        "The controller's last good scan, as ForeSite recorded it. Not the time it was read.";
    private const string PointTime = "The CygNet point's own time stamp. Some sites hold old values, so allow for age.";

    private static readonly Dictionary<string, AdapterQuantity[]> Quantities = new(StringComparer.OrdinalIgnoreCase)
    {
        [AdapterReadRules.ForeSiteKey] =
        [
            new("PumpFillage", "Pump fillage", AdapterValueKinds.Number, "%", ControllerScan),
            new("AveragePumpFillage", "Average pump fillage", AdapterValueKinds.Number, "%", ControllerScan),
            new(ForeSiteSurfaceCards, "Surface cards", AdapterValueKinds.Structured, null,
                "When each card was captured. Several cards may arrive together.",
                ForeSiteSurfaceCardsSchemaId, StructuredCurrentCapability)
        ],
        [AdapterReadRules.CygNetKey] =
        [
            new("PumpFillage", "Pump fillage", AdapterValueKinds.Number, "%", PointTime),
            new("CasingPressure", "Casing pressure", AdapterValueKinds.Number, "psi", PointTime),
            new("TubingPressure", "Tubing pressure", AdapterValueKinds.Number, "psi", PointTime),
            new("RodLoad", "Rod load", AdapterValueKinds.Number, "lbf", PointTime),
            new("MotorAmps", "Motor current", AdapterValueKinds.Number, "A", PointTime, NeedsMapping: true)
        ]
    };

    private static readonly Dictionary<string, TimeSpan> ReadBudgets = new(StringComparer.OrdinalIgnoreCase)
    {
        [AdapterReadRules.ForeSiteKey] = TimeSpan.FromSeconds(6),
        [AdapterReadRules.CygNetKey] = TimeSpan.FromSeconds(10)
    };

    /// <summary>How long the Gateway waits for one value from the adapter, or null for an adapter that serves no reads.</summary>
    public static TimeSpan? ReadBudget(string? adapterKey) =>
        adapterKey is not null && ReadBudgets.TryGetValue(adapterKey, out var budget) ? budget : null;

    /// <summary>The quantities the adapter describes, or an empty list for an adapter that serves no reads.</summary>
    public static IReadOnlyList<AdapterQuantity> For(string? adapterKey) =>
        adapterKey is not null && Quantities.TryGetValue(adapterKey, out var list) ? list : [];
}
