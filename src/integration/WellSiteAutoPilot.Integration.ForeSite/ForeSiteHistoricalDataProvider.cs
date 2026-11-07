using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Selectively reconciled from the time-range POC. Uses the existing authenticated session.</summary>
public sealed class ForeSiteHistoricalDataProvider(ForeSiteSession session) : IBoundedHistoricalDataProvider
{
    public async ValueTask<HistoricalReadResult> ReadHistoryAsync(HistoricalReadRequest request, CancellationToken cancellationToken = default)
    {
        HistoricalReadLimits.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the whole-second route format established by the time-range POC. Refuse unsupported
        // precision rather than silently rounding requested bounds or guessing a new ForeSite route format.
        if (request.StartUtc.Ticks % TimeSpan.TicksPerSecond != 0 || request.EndUtc.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ForeSiteMeasurementException("FORESITE_HISTORY_PRECISION_UNSUPPORTED");
        if (!long.TryParse(request.AssetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId) || wellId <= 0)
            throw new ForeSiteMeasurementException("FORESITE_WELL_ID_INVALID");
        var sourceQuantity = request.Quantity switch
        {
            "PumpFillage" => "CurrentPumpFillage",
            _ => throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED")
        };
        static string Iso(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        using var response = await session.PostAsync(
            $"Surveillance/CygNetTrends/{wellId}/{Iso(request.StartUtc)}/{Iso(request.EndUtc)}", new[] { sourceQuantity }, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new ForeSiteMeasurementException("FORESITE_READ_FAILED");
        try
        {
            // Bound the stream before parsing, including chunked responses without a Content-Length.
            if (response.Content.Headers.ContentLength > HistoricalReadLimits.MaximumSourceBytes)
                throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (body.Length + count > HistoricalReadLimits.MaximumSourceBytes)
                    throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");
                body.Write(buffer, 0, count);
            }
            var bytes = body.ToArray();
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            RejectDuplicates(root);
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var item = root[0];
            if (item.ValueKind != JsonValueKind.Object || Text(item, "ErrorMessage") is not { } status ||
                !string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
                throw new ForeSiteMeasurementException("FORESITE_TREND_FAILED");
            var unit = Text(item, "Units");
            if (string.IsNullOrWhiteSpace(unit) || unit.Length > 64) throw new ForeSiteMeasurementException("FORESITE_UNIT_MISSING");
            if (!item.TryGetProperty("PointValues", out var points) || points.ValueKind != JsonValueKind.Array)
                throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
            var samples = new List<EngineeringValue>();
            var retrieved = DateTimeOffset.UtcNow;
            foreach (var point in points.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stamp = point.ValueKind == JsonValueKind.Object ? Text(point, "Timestamp") : null;
                if (stamp is null || !(stamp.EndsWith('Z') || stamp.Length >= 25 && stamp[^6] is '+' or '-' && stamp[^3] == ':') ||
                    !DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new ForeSiteMeasurementException("FORESITE_TIMESTAMP_INVALID");
                if (!point.TryGetProperty("Value", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetDecimal(out var value))
                    throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_INVALID");
                if (time < request.StartUtc || time >= request.EndUtc) continue;
                // The recorded trend contract has no quality field. Do not invent Good.
                samples.Add(new(request.Quantity, value, unit, time.ToUniversalTime(), "Unknown", "foresite:trends", retrieved, "HistorianSample"));
            }
            var ordered = samples.OrderBy(sample => sample.TimestampUtc).Take(request.MaximumSamples).ToArray();
            var truncated = samples.Count > ordered.Length;
            var result = new HistoricalReadResult(request.Quantity, "foresite:trends", Text(item, "PointUDC"), "Raw",
                request.StartUtc.ToUniversalTime(), request.EndUtc.ToUniversalTime(), ordered, truncated,
                truncated ? ["SOURCE_QUALITY_UNAVAILABLE", "SAMPLE_LIMIT_REACHED"] : ["SOURCE_QUALITY_UNAVAILABLE"]);
            if (JsonSerializer.SerializeToUtf8Bytes(result).Length > MeasurementPayloadLimits.MaximumReplyBytes)
                throw new ForeSiteMeasurementException("FORESITE_PAYLOAD_TOO_LARGE");
            return result;
        }
        catch (JsonException) { throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID"); }
    }

    public async ValueTask<IReadOnlyCollection<EngineeringValue>> GetHistoryAsync(string assetExternalId, string quantity,
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default)
    {
        var result = await ReadHistoryAsync(new(assetExternalId, quantity, startUtc, endUtc), cancellationToken);
        if (result.Truncated) throw new ForeSiteMeasurementException("FORESITE_HISTORY_TRUNCATED");
        return result.Samples;
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ForeSiteMeasurementException("FORESITE_RESPONSE_INVALID");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
