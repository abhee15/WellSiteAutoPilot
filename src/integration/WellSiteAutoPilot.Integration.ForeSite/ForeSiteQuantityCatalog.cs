using System.Collections.Frozen;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One current quantity ForeSite serves from its well status document. The value lives in
/// <c>Value.{ValueField}</c> and its unit, precision and range in <c>Units.{UnitField}</c>.
/// The fallback unit and range apply only when ForeSite sends no unit entry.
/// </summary>
public sealed record ForeSiteQuantity(
    string Key, string ValueField, string? UnitField = null, string FallbackUnit = "",
    decimal? FallbackMin = null, decimal? FallbackMax = null);

public static class ForeSiteQuantityCatalog
{
    public static readonly IReadOnlyList<ForeSiteQuantity> All =
    [
        new("PumpFillage", "CurrentPumpFillage", "CurrentPumpFillage", "%", 0, 100),
        new("AveragePumpFillage", "AveragePumpFillage", "AveragePumpFillage", "%", 0, 100)
    ];

    private static readonly FrozenDictionary<string, ForeSiteQuantity> ByKey =
        All.ToFrozenDictionary(item => item.Key, StringComparer.Ordinal);

    public static ForeSiteQuantity? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var item) ? item : null;
}
