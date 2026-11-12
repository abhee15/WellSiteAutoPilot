using System.Globalization;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>
/// The engineering units a module can declare, and the conversions between compatible ones. Compatibility is by
/// dimension: psi and kPa convert, psi and degF do not. Gauge and absolute pressure are separate dimensions because
/// converting between them needs an atmospheric pressure the module does not know; "psi" alone says neither, so it
/// does not convert to psia or psig. An unknown unit converts only to itself.
/// </summary>
public static class Units
{
    private sealed record Unit(string Key, string Dimension, decimal Factor, decimal Offset = 0m);

    private static readonly Unit[] Table =
    [
        new("Pa", "pressure", 0.001m), new("kPa", "pressure", 1m), new("MPa", "pressure", 1000m), new("bar", "pressure", 100m),
        new("psi", "pressure", 6.894757293168m),
        new("kPaa", "pressure-absolute", 1m), new("bara", "pressure-absolute", 100m), new("psia", "pressure-absolute", 6.894757293168m),
        new("kPag", "pressure-gauge", 1m), new("barg", "pressure-gauge", 100m), new("psig", "pressure-gauge", 6.894757293168m),
        new("degC", "temperature", 1m), new("degF", "temperature", 5m / 9m, -32m), new("K", "temperature", 1m, -273.15m),
        new("m", "length", 1m), new("ft", "length", 0.3048m), new("in", "length", 0.0254m), new("mm", "length", 0.001m),
        new("bbl/d", "volume-rate", 1m), new("m3/d", "volume-rate", 6.289810770432m), new("bbl/h", "volume-rate", 24m),
        new("mscf/d", "gas-rate", 1m), new("scf/d", "gas-rate", 0.001m), new("mcf/d", "gas-rate", 1m),
        new("%", "ratio", 1m), new("fraction", "ratio", 100m),
        new("s", "time", 1m), new("min", "time", 60m), new("h", "time", 3600m), new("d", "time", 86400m),
        new("spm", "stroke-rate", 1m), new("Hz", "frequency", 1m),
        new("A", "current", 1m), new("V", "voltage", 1m), new("kW", "power", 1m), new("hp", "power", 0.745699872m),
        new("deg", "angle", 1m), new("lbf", "force", 1m), new("N", "force", 0.224808943m), new("kN", "force", 224.808943m)
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["°F"] = "degF", ["F"] = "degF", ["°C"] = "degC", ["C"] = "degC", ["pct"] = "%", ["percent"] = "%", ["lb"] = "lbf",
        ["m³/d"] = "m3/d", ["bpd"] = "bbl/d", ["BPD"] = "bbl/d", ["bblpd"] = "bbl/d", ["MCF/D"] = "mcf/d", ["kPa(a)"] = "kPaa"
    };

    private static readonly Dictionary<string, Unit> ByKey = Table.ToDictionary(u => u.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Known => Table.Select(u => u.Key).ToList();

    /// <summary>The canonical spelling of a unit, or null for none. Unknown units are kept as written.</summary>
    public static string? Normalize(string? unit)
    {
        var text = unit?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (Aliases.TryGetValue(text, out var alias)) text = alias;
        return ByKey.TryGetValue(text, out var known) ? known.Key : text;
    }

    public static string? DimensionOf(string? unit) => unit is not null && ByKey.TryGetValue(Normalize(unit) ?? "", out var u) ? u.Dimension : null;

    /// <summary>Converts a value between units, or explains why it cannot.</summary>
    public static bool TryConvert(decimal value, string? from, string? to, out decimal result, out string? problem)
    {
        result = value;
        problem = null;
        var source = Normalize(from);
        var target = Normalize(to);
        if (source is null || target is null || string.Equals(source, target, StringComparison.Ordinal))
            return true;
        if (!ByKey.TryGetValue(source, out var a) || !ByKey.TryGetValue(target, out var b))
        {
            problem = $"'{source}' cannot be converted to '{target}': at least one of them is not a unit this version knows.";
            return false;
        }
        if (a.Dimension != b.Dimension)
        {
            problem = a.Dimension.StartsWith("pressure", StringComparison.Ordinal) && b.Dimension.StartsWith("pressure", StringComparison.Ordinal)
                ? $"'{source}' and '{target}' cannot be converted: they differ in gauge or absolute reference, or one does not say which it is."
                : $"'{source}' ({a.Dimension}) and '{target}' ({b.Dimension}) measure different things.";
            return false;
        }
        try
        {
            result = (value + a.Offset) * a.Factor / b.Factor - b.Offset;
            return true;
        }
        catch (OverflowException)
        {
            problem = "The converted value is out of range.";
            return false;
        }
    }

    /// <summary>
    /// The reference-free form of an absolute or gauge pressure unit (psia and psig both give psi). A difference between
    /// two pressures with the same reference is a plain pressure, so a module may publish it that way.
    /// </summary>
    public static string? WithoutReference(string? unit) =>
        Normalize(unit) is { } key && ByKey.TryGetValue(key, out var u) && u.Dimension is "pressure-absolute" or "pressure-gauge" ? key[..^1] : null;

    public static string Format(decimal value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    public static string Format(decimal? value, string whenMissing) => value is null ? whenMissing : Format(value.Value);
}
