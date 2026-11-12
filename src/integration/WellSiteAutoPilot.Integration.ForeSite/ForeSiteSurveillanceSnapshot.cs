using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One authorized ForeSite well-status acquisition, exposing only the two reviewed controller
/// quantities. This is not an applicability catalog: numeric WellQuantity IDs are unmapped.
/// </summary>
public static class ForeSiteSurveillanceSnapshot
{
    public const int MaximumQuantities = 2;

    public static async Task<IReadOnlyDictionary<string, EngineeringValue>> ReadAsync(
        ForeSiteSession session, string externalId, IReadOnlyCollection<string> quantities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(quantities);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        if (quantities.Count is < 1 or > MaximumQuantities)
            throw new ForeSiteMeasurementException("FORESITE_SNAPSHOT_QUANTITY_LIMIT");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var quantity in quantities)
        {
            // SourceField is deliberately the existing *two-quantity* production allowlist.
            var field = ForeSiteMeasurements.SourceField(quantity);
            if (!fields.TryAdd(quantity, field))
                throw new ForeSiteMeasurementException("FORESITE_SNAPSHOT_QUANTITY_DUPLICATE");
        }

        // Validate every requirement before the source call. No cross-execution status cache.
        using var response = await session.GetAsync(
            "Surveillance/well/" + wellId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));

        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");

            // Validate all required values before returning any result. A partial snapshot is refused.
            var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (quantity, field) in fields)
            {
                if (!value.TryGetProperty(field, out var reading) || reading.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
                if (reading.ValueKind != JsonValueKind.Number || !reading.TryGetDecimal(out var number) ||
                    number is < 0 or > 100)
                    throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
                numbers.Add(quantity, number);
            }

            if (!value.TryGetProperty("LastGoodScanTime", out var scan) || scan.ValueKind != JsonValueKind.String ||
                !TryOffsetTime(scan.GetString(), out var acquiredAt))
                throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");

            // One acquisition for all fields and one retrieval event. LastScanTime is never
            // substituted; the reviewed host separately evaluates data age/future policy.
            var retrievedAt = DateTimeOffset.UtcNow;
            return numbers.ToDictionary(pair => pair.Key,
                pair => new EngineeringValue(pair.Key, pair.Value, "%", acquiredAt, "Good",
                    "foresite:controller-scan", retrievedAt, "ControllerScan"),
                StringComparer.Ordinal);
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    internal static bool TryOffsetTime(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return text is { Length: >= 20 } &&
            (text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) &&
            (timestamp = timestamp.ToUniversalTime()) != default;
    }
}
