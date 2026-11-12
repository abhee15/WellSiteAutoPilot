using System.Globalization;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>
/// Reads an input from history instead of the current value: the reading in a short window that ends at a local time of day
/// some days back (for example "yesterday at the gauge-off time"). AtLocal is "HH:mm" in the site's time zone, or the name of a
/// parameter holding that time. Reduce picks one value from the window: last, first, avg, min or max.
/// </summary>
public sealed record InputHistory(int DaysBack, string AtLocal, int WindowMinutes = 10, string Reduce = "last");

public static class InputHistoryWindow
{
    public static readonly string[] Reductions = ["last", "first", "avg", "min", "max"];

    public static string? Validate(InputHistory history, IReadOnlyList<DraftParameter> parameters)
    {
        if (history.DaysBack is < 0 or > 30) return "Days back must be from 0 to 30.";
        if (history.WindowMinutes is < 1 or > 1440) return "The window must be from 1 minute to 24 hours.";
        if (!Reductions.Contains(history.Reduce, StringComparer.Ordinal)) return $"Unknown reduction '{history.Reduce}'. Use one of {string.Join(", ", Reductions)}.";
        var text = history.AtLocal;
        var named = parameters.FirstOrDefault(p => p.Name == history.AtLocal);
        if (named is not null) text = named.Value;
        else if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "Time of day must be HH:mm in site time, or the name of a parameter that holds it.";
        return TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? null : $"Parameter {history.AtLocal} must hold a time written HH:mm.";
    }

    /// <summary>The UTC interval to read, or null with a reason when the time of day cannot be understood.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? Resolve(
        InputHistory history, IReadOnlyDictionary<string, string> parameterValues, DateTimeOffset now, TimeZoneInfo site, out string? problem)
    {
        var text = parameterValues.TryGetValue(history.AtLocal, out var named) ? named : history.AtLocal;
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            problem = $"'{text}' is not a time of day written HH:mm.";
            return null;
        }
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, site).DateTime).AddDays(-history.DaysBack);
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // A time skipped by a clock change moves forward to the first valid instant.
        if (site.IsInvalidTime(local)) local = local.AddHours(1);
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, site), TimeSpan.Zero);
        problem = null;
        return (end.AddMinutes(-history.WindowMinutes), end);
    }

    public static decimal? Reduce(string how, IReadOnlyList<decimal> values) => values.Count == 0 ? null : how switch
    {
        "first" => values[0],
        "avg" => values.Average(),
        "min" => values.Min(),
        "max" => values.Max(),
        _ => values[^1]
    };
}
