using System.Globalization;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>
/// How a CygNet point record becomes a number and a quality. CygNet hands values back as padded text, and a point that
/// exists but has never received data comes back as an empty string with only the Initialized flag.
/// </summary>
public static class CygNetValues
{
    public const string NoValueCode = "CYGNET_POINT_NO_VALUE";
    public const string NotNumericCode = "CYGNET_VALUE_NOT_NUMERIC";

    // Healthy points carry housekeeping flags and data-type bits; only another flag is a quality concern.
    private static readonly HashSet<string> NeutralFlags = new(StringComparer.OrdinalIgnoreCase)
        { "None", "0", "Initialized", "Updated", "StringData", "DigitalData", "OutputData" };

    /// <summary>The numeric value of a point record, or a failure that says why it has none.</summary>
    public static decimal ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new WellSiteAutoPilotException(NoValueCode, FailureKind.DependencyUnavailable,
                "The point exists but has not received a value yet.", retryable: true);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new WellSiteAutoPilotException(NotNumericCode, FailureKind.Validation,
                "The point holds text or a state, not a number. Map the quantity to a numeric point in the connection's point mapping.");
        return number;
    }

    /// <summary>Unreliable is Bad; any flag beyond the housekeeping ones is Uncertain; otherwise Good.</summary>
    public static string Quality(string? statusFlags)
    {
        var flags = (statusFlags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
        if (flags.Contains("Unreliable", StringComparer.OrdinalIgnoreCase)) return "Bad";
        return flags.All(NeutralFlags.Contains) ? "Good" : "Uncertain";
    }

    /// <summary>A short age for people: "45 s", "16 min", "3.5 h", "12 days", "6.0 years".</summary>
    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        var culture = CultureInfo.InvariantCulture;
        if (age.TotalSeconds < 90) return string.Create(culture, $"{age.TotalSeconds:0} s");
        if (age.TotalMinutes < 90) return string.Create(culture, $"{age.TotalMinutes:0} min");
        if (age.TotalHours < 48) return string.Create(culture, $"{age.TotalHours:0.#} h");
        if (age.TotalDays < 730) return string.Create(culture, $"{age.TotalDays:0} days");
        return string.Create(culture, $"{age.TotalDays / 365.25:0.0} years");
    }
}
