using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One production day for a well. StartUtc and EndUtc are ForeSite's production-day boundaries (05:00Z for a US Central well
/// in daylight time), so the day edge belongs to the asset, not to the module. Only numeric, non-null fields are returned.
/// </summary>
public sealed record ForeSiteDailyAverage(DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyDictionary<string, decimal> Values);

public sealed record ForeSiteDailyAverages(IReadOnlyList<ForeSiteDailyAverage> Days, IReadOnlyDictionary<string, string> Units);

/// <summary>Read-only daily values from <c>Surveillance/Well/DailyAverage/{wellId}/{start}/{end}</c> (GET, ISO 8601 dates).</summary>
public static class ForeSiteDailyAverageReader
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "Id", "WellId", "WellTestId" };

    public static async Task<ForeSiteDailyAverages> ReadAsync(ForeSiteSession session, string externalId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(366)) throw new ForeSiteMeasurementException("FORESITE_RANGE_INVALID");
        static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        using var response = await session.GetAsync(
            $"Surveillance/Well/DailyAverage/{wellId.ToString(CultureInfo.InvariantCulture)}/{Iso(fromUtc)}/{Iso(toUtc)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var units = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("Units", out var unitBlock) && unitBlock.ValueKind == JsonValueKind.Object)
                foreach (var unit in unitBlock.EnumerateObject())
                    if (unit.Value.ValueKind == JsonValueKind.Object && unit.Value.TryGetProperty("UnitKey", out var key) &&
                        key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 and <= 32 } text) units[unit.Name] = text;
            var days = new List<ForeSiteDailyAverage>();
            foreach (var day in values.EnumerateArray())
            {
                if (day.ValueKind != JsonValueKind.Object || !Time(day, "StartDateTime", out var start) || !Time(day, "EndDateTime", out var end)) continue;
                var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var field in day.EnumerateObject())
                    if (!Skipped.Contains(field.Name) && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetDecimal(out var number))
                        numbers[field.Name] = number;
                days.Add(new(start, end, numbers));
            }
            return new(days.OrderBy(d => d.StartUtc).ToList(), units);
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    // A day without an explicit UTC offset is rejected: guessing a zone would shift the production day.
    private static bool Time(JsonElement day, string name, out DateTimeOffset value)
    {
        value = default;
        if (!day.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String || item.GetString() is not { } text ||
            !(text.EndsWith('Z') || text.LastIndexOfAny(['+', '-']) > 10) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return false;
        value = value.ToUniversalTime();
        return true;
    }
}
