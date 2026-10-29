using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Stored surface-card library reads; never initiates a controller scan.</summary>
public static class ForeSiteCards
{
    public const string Quantity = "SurfaceCards";
    public const string SchemaId = "foresite.surface-cards/v1";
    private static readonly (int Type, string Kind)[] Kinds = [(1, "CURRENT"), (2, "FULL")];
    private const int MaximumCards = 16;
    private const int MaximumPoints = 1024;

    public static async Task<EngineeringValue> ReadAsync(ForeSiteSession session, string externalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw Failure("FORESITE_WELL_ID_INVALID");
        var cards = new List<object>();
        DateTimeOffset? oldest = null;
        foreach (var (type, kind) in Kinds)
        {
            using var response = await session.GetAsync($"Dynacards/Dynacard/LatestWithUnits/Lib/{wellId}/{type}", cancellationToken,
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) throw Failure("FORESITE_READ_FAILED");
            try
            {
                var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicates(document.RootElement);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    root.TryGetProperty("FullErrorMessage", out var error) && error.ValueKind != JsonValueKind.Null &&
                        (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString())) ||
                    !root.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
                    throw Failure("FORESITE_RESPONSE_INVALID");
                if (!value.TryGetProperty("SurfaceCards", out var surface) || surface.ValueKind != JsonValueKind.Array)
                    throw Failure("FORESITE_RESPONSE_INVALID");
                if (surface.GetArrayLength() == 0) continue;
                var captured = Timestamp(value);
                if (captured > DateTimeOffset.UtcNow.AddSeconds(30)) throw Failure("FORESITE_TIMESTAMP_INVALID");
                if (!root.TryGetProperty("Units", out var units)) throw Failure("FORESITE_UNIT_MISSING");
                var positionUnit = Unit(units, "Position");
                var loadUnit = Unit(units, "Load");
                // Preserve the source symbols: lb is not silently relabelled as lbf.
                if (positionUnit != "in" || loadUnit != "lb") throw Failure("FORESITE_UNIT_UNSUPPORTED");
                var index = 0;
                foreach (var card in surface.EnumerateArray())
                {
                    if (cards.Count >= MaximumCards) throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
                    if (card.ValueKind != JsonValueKind.Object || !card.TryGetProperty("Points", out var points) ||
                        points.ValueKind != JsonValueKind.Array || points.GetArrayLength() is < 2 or > MaximumPoints)
                        throw Failure("FORESITE_CARD_POINTS_INVALID");
                    var position = new List<decimal>();
                    var load = new List<decimal>();
                    foreach (var point in points.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                            point[0].ValueKind != JsonValueKind.Number || point[1].ValueKind != JsonValueKind.Number ||
                            !point[0].TryGetDecimal(out var x) || !point[1].TryGetDecimal(out var y) ||
                            x is < -1000000 or > 1000000 || y is < -1000000 or > 1000000)
                            throw Failure("FORESITE_CARD_POINTS_INVALID");
                        position.Add(x); load.Add(y);
                    }
                    cards.Add(new
                    {
                        id = $"{kind}-{captured:yyyyMMddTHHmmssfffffffZ}-{index++}", kind, representation = "SURFACE",
                        capturedAtUtc = captured.ToString("O", CultureInfo.InvariantCulture),
                        source = "foresite:dynacard-library", positionUnit, loadUnit, position, load
                    });
                }
                if (oldest is null || captured < oldest) oldest = captured;
            }
            catch (JsonException) { throw Failure("FORESITE_RESPONSE_INVALID"); }
        }
        if (cards.Count == 0 || oldest is null) throw Failure("FORESITE_MEASUREMENT_MISSING");
        var data = JsonSerializer.SerializeToElement(cards);
        if (System.Text.Encoding.UTF8.GetByteCount(data.GetRawText()) > MeasurementPayloadLimits.MaximumValueBytes)
            throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
        // Library availability supplies no quality code. The host's existing policy rejects Unknown.
        return new(Quantity, null, "", oldest.Value, "Unknown", "foresite:dynacard-library",
            DateTimeOffset.UtcNow, "CardCapture") { StructuredValue = new(SchemaId, data) };
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MeasurementPayloadLimits.MaximumReplyBytes)
            throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            // Read at most one byte beyond the ceiling, including chunked responses.
            var remaining = MeasurementPayloadLimits.MaximumReplyBytes - (int)bytes.Length + 1;
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > MeasurementPayloadLimits.MaximumReplyBytes)
                throw Failure("FORESITE_PAYLOAD_TOO_LARGE");
            await bytes.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static DateTimeOffset Timestamp(JsonElement value)
    {
        if (!value.TryGetProperty("Timestamp", out var time) || time.ValueKind != JsonValueKind.String ||
            time.GetString() is not { } text ||
            !(text.EndsWith('Z') || text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-') ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var captured) || captured == default)
            throw Failure("FORESITE_TIMESTAMP_INVALID");
        return captured.ToUniversalTime();
    }

    private static string Unit(JsonElement units, string name) =>
        units.ValueKind == JsonValueKind.Object && units.TryGetProperty(name, out var unit) && unit.ValueKind == JsonValueKind.Object &&
        unit.TryGetProperty("UnitKey", out var key) && key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 and <= 32 } text
            ? text : throw Failure("FORESITE_UNIT_MISSING");

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in element.EnumerateObject())
            {
                if (!names.Add(item.Name)) throw Failure("FORESITE_RESPONSE_INVALID");
                RejectDuplicates(item.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
    private static ForeSiteMeasurementException Failure(string code) => new(code);
}
