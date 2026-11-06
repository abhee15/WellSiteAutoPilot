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
        var field = quantity switch
        {
            "PumpFillage" => "CurrentPumpFillage",
            "AveragePumpFillage" => "AveragePumpFillage",
            _ => throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED")
        };
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
            if (reading.ValueKind != JsonValueKind.Number || !reading.TryGetDecimal(out var number) || number is < 0 or > 100)
                throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
            if (!value.TryGetProperty("LastGoodScanTime", out var time) || time.ValueKind != JsonValueKind.String ||
                !TryOffsetTime(time.GetString(), out var timestamp))
                throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");
            // Owner-confirmed ForeSite/CygNet contract: these controller fields are returned
            // by a successful datagroup scan. LastScanTime is only the latest attempt;
            // never substitute it, retrieval time, or a well-test date for acquisition time.
            // Good means successful acquisition of a valid controller value, not independent
            // sensor validation. The host separately enforces the module's age/future policy.
            return new(quantity, number, "%", timestamp, "Good", "foresite:controller-scan",
                DateTimeOffset.UtcNow, "ControllerScan");
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    // One status document carries every current quantity of the well, so a cached copy serves all of them.
    private static async Task<byte[]> ReadStatusAsync(ForeSiteSession session, long wellId, ForeSiteStatusCache? cache,
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
