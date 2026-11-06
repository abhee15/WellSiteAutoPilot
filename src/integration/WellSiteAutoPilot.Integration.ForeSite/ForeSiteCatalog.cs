using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// Builds the quantity catalog for one well from its status document: a quantity is listed when ForeSite sends a
/// numeric value for it (an AEBEAM well has no fillage, so none is listed), with the unit and range ForeSite reports.
/// The device type comes from the same document.
/// </summary>
public static class ForeSiteCatalog
{
    public static async Task<AssetQuantityCatalog> ReadAsync(ForeSiteSession session, string externalId,
        CancellationToken cancellationToken, ForeSiteStatusCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var body = await ForeSiteMeasurements.ReadStatusAsync(session, wellId, cache, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var entries = new List<QuantityCatalogEntry>();
            foreach (var quantity in ForeSiteQuantityCatalog.All)
            {
                if (!value.TryGetProperty(quantity.ValueField, out var reading) || reading.ValueKind != JsonValueKind.Number ||
                    !reading.TryGetDecimal(out var number) || quantity.UnsetValue == number) continue;
                var (unit, min, max) = ForeSiteMeasurements.UnitAndRange(root, quantity);
                entries.Add(new(quantity.Key, quantity.Key, "current", unit,
                    min == decimal.MinValue ? null : min, max == decimal.MaxValue ? null : max));
            }
            return new(externalId, Text(value, "DeviceType"), Text(value, "WellType"), entries);
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 and <= 64 } text
            ? text : null;
}
