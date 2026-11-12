using System.Globalization;
using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Application.Authoring;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValueKind { Number, Flag, Text, Unavailable }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DataQuality { Good, Uncertain, Bad }

/// <summary>
/// A calculated or read value. "Unavailable" is a first-class state and is never a zero: a missing input must not
/// look like a valid calculated zero, and it carries the reason it is missing. A value also carries its unit, when it
/// was observed (the oldest observation among everything it was calculated from) and its quality (the worst of them),
/// so a result built on a stale or doubtful reading says so.
/// </summary>
public sealed record Val(
    ValueKind Kind, decimal? Number = null, bool? Flag = null, string? Text = null, string? Reason = null,
    string? Unit = null, DateTimeOffset? AsOf = null, DataQuality Quality = DataQuality.Good)
{
    [JsonIgnore]
    public bool Available => Kind != ValueKind.Unavailable;

    public static Val Of(decimal number, string? unit = null) => new(ValueKind.Number, Number: number, Unit: Units.Normalize(unit));
    public static Val Of(double number) => double.IsFinite(number) && Math.Abs(number) < 7.9e27 ? Of((decimal)number) : Missing("the value is out of range");
    public static Val Of(bool flag) => new(ValueKind.Flag, Flag: flag);
    public static Val OfText(string text) => new(ValueKind.Text, Text: text);
    public static Val Missing(string reason) => new(ValueKind.Unavailable, Reason: reason);

    /// <summary>Numbers and flags written as text become typed values, so parameters and samples can be plain strings.</summary>
    public static Val Parse(string? raw, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Missing("no value");
        var text = raw.Trim();
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return Of(number, unit);
        if (bool.TryParse(text, out var flag))
            return Of(flag);
        return OfText(text);
    }

    /// <summary>This value, stamped with the provenance of everything it was derived from.</summary>
    public Val Derived(params Val[] from)
    {
        var at = this.AsOf;
        var quality = this.Quality;
        foreach (var source in from)
        {
            if (source.AsOf is { } time && (at is null || time < at)) at = time;
            if (source.Quality > quality) quality = source.Quality;
        }
        return this with { AsOf = at, Quality = quality };
    }

    public string Display() => Kind switch
    {
        ValueKind.Number => Units.Format(Number!.Value) + (Unit is null ? "" : " " + Unit),
        ValueKind.Flag => Flag!.Value ? "true" : "false",
        ValueKind.Text => Text ?? "",
        _ => "unavailable: " + Reason
    };
}
