using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Integration.ForeSite;

public sealed record ForeSiteTrendPoint(DateTimeOffset TimestampUtc, decimal Value);

/// <summary>History of one quantity. Error is null on success; otherwise ForeSite's message (for example an unknown quantity enum).</summary>
public sealed record ForeSiteTrend(string Quantity, string? Unit, string? SourceUdc, string? Error, IReadOnlyList<ForeSiteTrendPoint> Points);

/// <summary>
/// Read-only time-range history from <c>Surveillance/CygNetTrends/{wellId}/{start}/{end}</c>. The route is a POST only because
/// the quantity list travels in the body; it reads and never writes. Quantity names are ForeSite's enum names (CurrentPumpFillage,
/// CurrentSPM, RunTimeToday). ForeSite answers per quantity, so one unknown name does not fail the others. Points are about five
/// minutes apart, with extra points at events. Units can differ from the status document (run time is hours here, seconds there),
/// so the unit comes from this response and is never assumed.
/// </summary>
public static class ForeSiteTrends
{
    public const int MaximumQuantities = 16;

    public static async Task<IReadOnlyList<ForeSiteTrend>> ReadAsync(ForeSiteSession session, string externalId,
        IReadOnlyList<string> quantities, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(quantities);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        if (quantities.Count is 0 or > MaximumQuantities || quantities.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 64))
            throw new ForeSiteMeasurementException("FORESITE_QUANTITIES_INVALID");
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(31)) throw new ForeSiteMeasurementException("FORESITE_RANGE_INVALID");
        static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        using var response = await session.PostAsync(
            $"Surveillance/CygNetTrends/{wellId.ToString(CultureInfo.InvariantCulture)}/{Iso(fromUtc)}/{Iso(toUtc)}", quantities, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var result = new List<ForeSiteTrend>();
            var index = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                // ForeSite answers in request order, and an error entry carries no udc, so position names the quantity.
                var quantity = index < quantities.Count ? quantities[index] : "";
                index++;
                if (item.ValueKind != JsonValueKind.Object) continue;
                var message = Text(item, "ErrorMessage");
                var error = string.Equals(message, "Success", StringComparison.OrdinalIgnoreCase) ? null : message ?? "FORESITE_TREND_FAILED";
                var points = new List<ForeSiteTrendPoint>();
                if (error is null && item.TryGetProperty("PointValues", out var values) && values.ValueKind == JsonValueKind.Array)
                    foreach (var point in values.EnumerateArray())
                        if (point.ValueKind == JsonValueKind.Object && Text(point, "Timestamp") is { } stamp &&
                            (stamp.EndsWith('Z') || stamp.LastIndexOfAny(['+', '-']) > 10) &&
                            DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
                            point.TryGetProperty("Value", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDecimal(out var value))
                            points.Add(new(time.ToUniversalTime(), value));
                result.Add(new(quantity, Text(item, "Units"), Text(item, "PointUDC"), error, points.OrderBy(p => p.TimestampUtc).ToList()));
            }
            return result;
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
