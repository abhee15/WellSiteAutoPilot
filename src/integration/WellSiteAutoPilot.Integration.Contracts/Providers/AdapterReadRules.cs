namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// What an adapter lets the Server read directly from one imported source object: the resource kind its discovery
/// reports and the scalar current quantities it can serve. The Server asks here instead of knowing each adapter.
/// </summary>
public sealed record AdapterReadRule(string AdapterKey, string ResourceKind, IReadOnlySet<string> CurrentQuantities);

/// <summary>The adapters that can serve direct current reads for saved connections.</summary>
public static class AdapterReadRules
{
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
        new(ForeSiteKey, "foresite.well", new HashSet<string>(StringComparer.Ordinal) { "PumpFillage", "AveragePumpFillage" }),
        new(CygNetKey, CygNetResourceKind, CygNetQuantities)
    ];

    public static AdapterReadRule? Find(string? adapterKey) =>
        Rules.FirstOrDefault(rule => string.Equals(rule.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase));
}
