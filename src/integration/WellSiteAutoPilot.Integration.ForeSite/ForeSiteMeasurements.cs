using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public static class ForeSiteMeasurements
{
    public static async Task<EngineeringValue> ReadAsync(ForeSiteSession session, string externalId, string quantity,
        CancellationToken cancellationToken, ForeSiteStatusCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var map = ForeSiteQuantityCatalog.Find(quantity)
            ?? throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED");
        var field = map.ValueField;
        var body = await ReadStatusAsync(session, wellId, cache, cancellationToken);
        try
        {
            // HttpClient bounds buffered bytes; never deserialize arbitrary provider objects into module inputs.
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            if (!value.TryGetProperty(field, out var reading) || reading.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
            var (unit, min, max) = UnitAndRange(root, map);
            if (reading.ValueKind != JsonValueKind.Number || !reading.TryGetDecimal(out var number) ||
                number < min || number > max)
                throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
            if (map.UnsetValue == number) throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
            if (!value.TryGetProperty("LastGoodScanTime", out var time) || time.ValueKind != JsonValueKind.String ||
                !TryOffsetTime(time.GetString(), out var timestamp))
                throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");
            // Owner-confirmed ForeSite/CygNet contract: these controller fields are returned
            // by a successful datagroup scan. LastScanTime is only the latest attempt;
            // never substitute it, retrieval time, or a well-test date for acquisition time.
            // Good means successful acquisition of a valid controller value, not independent
            // sensor validation. The host separately enforces the module's age/future policy.
            return new(quantity, number, unit, timestamp, "Good", "foresite:controller-scan",
                DateTimeOffset.UtcNow, "ControllerScan");
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    // One status document carries every current quantity of the well, so a cached copy serves all of them.
    internal static async Task<byte[]> ReadStatusAsync(ForeSiteSession session, long wellId, ForeSiteStatusCache? cache,
        CancellationToken cancellationToken)
    {
        if (cache is not null && cache.TryGet(wellId, out var cached)) return cached;
        using var response = await session.GetAsync("Surveillance/well/" + wellId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!response.IsSuccessStatusCode)
            // The shared error parser retains FullErrorMessage within the Gateway boundary.
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        cache?.Set(wellId, body);
        return body;
    }

    // The unit is what ForeSite reports for this well. The catalog's range and unit apply only when ForeSite sends none.
    internal static (string Unit, decimal Min, decimal Max) UnitAndRange(JsonElement root, ForeSiteQuantity map)
    {
        var unit = map.FallbackUnit;
        var min = map.FallbackMin ?? decimal.MinValue;
        var max = map.FallbackMax ?? decimal.MaxValue;
        if (map.UnitField is not null && root.TryGetProperty("Units", out var units) && units.ValueKind == JsonValueKind.Object &&
            units.TryGetProperty(map.UnitField, out var entry) && entry.ValueKind == JsonValueKind.Object)
        {
            if (entry.TryGetProperty("UnitKey", out var key) && key.ValueKind == JsonValueKind.String &&
                key.GetString() is { Length: > 0 and <= 32 } text) unit = text;
            if (entry.TryGetProperty("Min", out var low) && low.ValueKind == JsonValueKind.Number && low.TryGetDecimal(out var lowValue)) min = lowValue;
            if (entry.TryGetProperty("Max", out var high) && high.ValueKind == JsonValueKind.Number && high.TryGetDecimal(out var highValue)) max = highValue;
        }
        return (unit, min, max);
    }

    private static bool TryOffsetTime(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return text is { Length: >= 20 } &&
            (text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) &&
            (timestamp = timestamp.ToUniversalTime()) != default;
    }
}

public sealed class ForeSiteMeasurementException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
}
