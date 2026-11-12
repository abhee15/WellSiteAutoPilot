using System.Globalization;
using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Checks a parameter value against the bounds the module declares for it.</summary>
public static class ParameterRange
{
    public static string? Violation(DraftParameter parameter, string? text)
    {
        if (parameter.Min is null && parameter.Max is null) return null;
        var value = Val.Parse(text, parameter.Unit);
        if (value.Kind != ValueKind.Number) return "must be a number.";
        var number = value.Number!.Value;
        if (parameter.Min is { } low && number < low) return $"({Units.Format(number)}) is below the minimum {Units.Format(low)}.";
        if (parameter.Max is { } high && number > high) return $"({Units.Format(number)}) is above the maximum {Units.Format(high)}.";
        return null;
    }
}

/// <summary>
/// The text of a recommendation: plain words with {Name} (the value and its unit) or {Name:0.#} (the number alone,
/// formatted) placeholders. Names are checked when the module is checked, so a typo never reaches an operator.
/// </summary>
public static partial class RecommendationText
{
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?::([^{}]*))?\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<string> References(string? text) =>
        string.IsNullOrEmpty(text) ? [] : Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    public static string Render(string? text, Func<string, Val> lookup)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Placeholder().Replace(text, match =>
        {
            var value = lookup(match.Groups[1].Value);
            if (!value.Available) return "(unavailable)";
            if (match.Groups[2].Success && value.Kind == ValueKind.Number)
            {
                try { return value.Number!.Value.ToString(match.Groups[2].Value, CultureInfo.InvariantCulture); }
                catch (FormatException) { return value.Display(); }
            }
            return value.Display();
        });
    }
}
