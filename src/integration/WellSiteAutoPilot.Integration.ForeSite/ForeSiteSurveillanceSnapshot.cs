using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One authorized ForeSite well-status acquisition of catalogued surveillance quantities
/// (<see cref="ForeSiteSurveillanceCatalogue"/>). A value's presence never proves applicability, and
/// a missing value stays missing rather than becoming zero.
/// </summary>
public static class ForeSiteSurveillanceSnapshot
{
    public static int MaximumQuantities => ForeSiteSurveillanceCatalogue.All.Count;

    public static async Task<IReadOnlyDictionary<string, EngineeringValue>> ReadAsync(
        ForeSiteSession session, string externalId, IReadOnlyCollection<string> quantities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(quantities);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        if (quantities.Count is < 1 || quantities.Count > MaximumQuantities)
            throw new ForeSiteMeasurementException("FORESITE_SNAPSHOT_QUANTITY_LIMIT");

        var entries = new Dictionary<string, ForeSiteSurveillanceQuantity>(StringComparer.Ordinal);
        foreach (var quantity in quantities)
        {
            if (!entries.TryAdd(quantity, ForeSiteMeasurements.Catalogued(quantity)))
                throw new ForeSiteMeasurementException("FORESITE_SNAPSHOT_QUANTITY_DUPLICATE");
        }

        // Validate every requirement before the source call. No cross-execution status cache.
        using var document = await GetStatusAsync(session, wellId, cancellationToken);
        try
        {
            var root = document.RootElement;
            var value = root.GetProperty("Value");
            // Validate all required values before returning any result. A partial snapshot is refused.
            root.TryGetProperty("Units", out var units);
            var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (quantity, entry) in entries)
            {
                if (!value.TryGetProperty(entry.Field, out var reading) || reading.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
                if (reading.ValueKind != JsonValueKind.Number || !reading.TryGetDecimal(out var number) ||
                    entry.IsPercent && number is < 0 or > 100)
                    throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
                // The document states each field's unit. A different unit system is refused, never converted.
                if (entry.Unit != ForeSiteSurveillanceQuantity.CountUnit && UnitKey(units, entry.Field) != entry.Unit)
                    throw new ForeSiteMeasurementException("FORESITE_UNIT_MISMATCH");
                numbers.Add(quantity, number);
            }

            if (!value.TryGetProperty("LastGoodScanTime", out var scan) || scan.ValueKind != JsonValueKind.String ||
                !TryOffsetTime(scan.GetString(), out var acquiredAt))
                throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");

            // One acquisition for all fields and one retrieval event. LastScanTime is never
            // substituted; the reviewed host separately evaluates data age/future policy.
            var retrievedAt = DateTimeOffset.UtcNow;
            return numbers.ToDictionary(pair => pair.Key,
                pair => new EngineeringValue(pair.Key, pair.Value, entries[pair.Key].Unit, acquiredAt, "Good",
                    "foresite:controller-scan", retrievedAt, "ControllerScan"),
                StringComparer.Ordinal);
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    /// <summary>
    /// One tolerant read of the whole catalogue for one well. Each field is reported as observed, missing, invalid or
    /// in an unexpected unit, so a gap in one field never hides the others. Nothing here proves applicability.
    /// </summary>
    public static async Task<ForeSiteSurveillanceReport> DescribeAsync(
        ForeSiteSession session, string externalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        using var document = await GetStatusAsync(session, wellId, cancellationToken);
        var root = document.RootElement;
        var value = root.GetProperty("Value");
        root.TryGetProperty("Units", out var units);
        var observations = ForeSiteSurveillanceCatalogue.All.Select(entry =>
        {
            if (!value.TryGetProperty(entry.Field, out var reading) || reading.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new ForeSiteSurveillanceObservation(entry.Quantity, entry.Unit, ForeSiteSurveillanceObservation.NoValue);
            if (reading.ValueKind != JsonValueKind.Number || !reading.TryGetDecimal(out var number) ||
                entry.IsPercent && number is < 0 or > 100)
                return new ForeSiteSurveillanceObservation(entry.Quantity, entry.Unit, ForeSiteSurveillanceObservation.Invalid);
            return entry.Unit != ForeSiteSurveillanceQuantity.CountUnit && UnitKey(units, entry.Field) != entry.Unit
                ? new ForeSiteSurveillanceObservation(entry.Quantity, UnitKey(units, entry.Field), ForeSiteSurveillanceObservation.UnitMismatch)
                : new ForeSiteSurveillanceObservation(entry.Quantity, entry.Unit, ForeSiteSurveillanceObservation.Observed);
        }).ToArray();
        return new(externalId, observations, DateTimeOffset.UtcNow);
    }

    private static async Task<JsonDocument> GetStatusAsync(ForeSiteSession session, long wellId, CancellationToken cancellationToken)
    {
        using var response = await session.GetAsync(
            "Surveillance/well/" + wellId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
        try
        {
            var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            }
            return document;
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    private static string? UnitKey(JsonElement units, string field) =>
        units.ValueKind == JsonValueKind.Object && units.TryGetProperty(field, out var unit) &&
        unit.ValueKind == JsonValueKind.Object && unit.TryGetProperty("UnitKey", out var key) &&
        key.ValueKind == JsonValueKind.String ? key.GetString() : null;

    private static bool TryOffsetTime(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return text is { Length: >= 20 } &&
            (text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) &&
            (timestamp = timestamp.ToUniversalTime()) != default;
    }
}
