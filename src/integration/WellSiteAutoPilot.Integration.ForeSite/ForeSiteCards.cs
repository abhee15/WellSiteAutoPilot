using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

// POC: read-only surface dynacards from the ForeSite dynacard library (Dynacards/Dynacard/LatestWithUnits/Lib).
public static class ForeSiteCards
{
    public const string SurfaceCardsQuantity = "SurfaceCards";
    public const string SchemaId = "foresite.surface-cards/v1";
    private static readonly (int Type, string Kind)[] CardKinds = [(1, "CURRENT"), (2, "FULL")];

    public static async Task<EngineeringValue> ReadAsync(ForeSiteSession session, string externalId, string quantity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (quantity != SurfaceCardsQuantity) throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED");
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");

        var cards = new JsonArray();
        DateTimeOffset? oldest = null;
        foreach (var (type, kind) in CardKinds)
        {
            using var response = await session.GetAsync(
                $"Dynacards/Dynacard/LatestWithUnits/Lib/{wellId}/{type}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ForeSiteMeasurementException("FORESITE_READ_FAILED",
                    await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", cancellationToken));
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                    new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
                var card = Map(document.RootElement, kind, out var captured);
                if (card is null) continue; // ForeSite reports "no dynacards found" per type; absence is not an error.
                cards.Add(card);
                if (oldest is null || captured < oldest) oldest = captured;
            }
            catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
        }
        if (cards.Count == 0 || oldest is null) throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
        // The oldest card sets the timestamp so the host's age policy is conservative across mixed card types.
        return new(quantity, 0m, "structured", oldest.Value, "Good", "foresite:dynacard-library",
            DateTimeOffset.UtcNow, "ControllerScan")
        { StructuredSchemaId = SchemaId, StructuredJson = cards.ToJsonString() };
    }

    private static JsonObject? Map(JsonElement root, string kind, out DateTimeOffset captured)
    {
        captured = default;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Value", out var value) ||
            value.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Units", out var units))
            throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
        if (!value.TryGetProperty("SurfaceCards", out var surface) || surface.ValueKind != JsonValueKind.Array ||
            surface.GetArrayLength() == 0) return null;
        var first = surface[0];
        if (!first.TryGetProperty("Points", out var points) || points.ValueKind != JsonValueKind.Array)
            throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
        if (!value.TryGetProperty("Timestamp", out var time) || time.ValueKind != JsonValueKind.String ||
            !time.GetString()!.EndsWith('Z') ||
            !DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out captured))
            throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");

        var position = new JsonArray();
        var load = new JsonArray();
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                !point[0].TryGetDecimal(out var x) || !point[1].TryGetDecimal(out var y))
                throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
            position.Add(x);
            load.Add(y);
        }
        return new JsonObject
        {
            ["id"] = $"{kind}-{captured:yyyyMMddTHHmmssZ}",
            ["kind"] = kind,
            ["representation"] = "SURFACE",
            ["capturedAtUtc"] = captured.ToString("O", CultureInfo.InvariantCulture),
            ["source"] = "foresite:dynacard-library",
            ["positionUnit"] = UnitKey(units, "Position"),
            ["loadUnit"] = UnitKey(units, "Load"),
            ["position"] = position,
            ["load"] = load
        };
    }

    private static string UnitKey(JsonElement units, string name) =>
        units.ValueKind == JsonValueKind.Object && units.TryGetProperty(name, out var unit) &&
        unit.TryGetProperty("UnitKey", out var key) && key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 } text
            ? text : throw new ForeSiteMeasurementException("FORESITE_UNIT_MISSING");
}
