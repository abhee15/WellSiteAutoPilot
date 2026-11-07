using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

public enum CygNetHistoryValueKind { FloatingPoint, SignedInteger, Text }

/// <summary>Vendor-independent representation of one VHS entry; the opt-in native client supplies these lazily.</summary>
public sealed record CygNetHistoryEntry(DateTime Timestamp, CygNetHistoryValueKind Kind, byte[] Value, string? StatusFlags);

/// <summary>Strict decoding and bounded projection of the POC's VHS reverse enumeration. No vendor assemblies required.</summary>
public static class CygNetHistoryValues
{
    public const int MaximumScannedEntries = 10000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static decimal Decode(CygNetHistoryValueKind kind, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MeasurementPayloadLimits.MaximumValueBytes) throw Failure("CYGNET_HISTORY_VALUE_INVALID");
        try
        {
            return kind switch
            {
                CygNetHistoryValueKind.FloatingPoint when bytes.Length == sizeof(double) =>
                    DecodeDouble(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes))),
                CygNetHistoryValueKind.SignedInteger when bytes.Length == sizeof(long) => BinaryPrimitives.ReadInt64LittleEndian(bytes),
                CygNetHistoryValueKind.Text when decimal.TryParse(StrictUtf8.GetString(bytes), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) => value,
                _ => throw Failure("CYGNET_HISTORY_VALUE_INVALID")
            };
        }
        catch (DecoderFallbackException) { throw Failure("CYGNET_HISTORY_VALUE_INVALID"); }
        catch (OverflowException) { throw Failure("CYGNET_HISTORY_VALUE_INVALID"); }
    }

    private static decimal DecodeDouble(double value) => double.IsFinite(value)
        ? (decimal)value : throw Failure("CYGNET_HISTORY_VALUE_INVALID");

    public static HistoricalReadResult ProjectReverse(HistoricalReadRequest request, string unit, string sourcePoint,
        IEnumerable<CygNetHistoryEntry> entries, DateTimeOffset? retrievedAtUtc = null, CancellationToken cancellationToken = default)
    {
        HistoricalReadLimits.Validate(request);
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(unit) || unit.Length > 12 || unit.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(sourcePoint) || sourcePoint.Length > 32 || sourcePoint.Any(char.IsControl))
            throw Failure("CYGNET_HISTORY_MAPPING_INVALID");
        var samples = new List<EngineeringValue>();
        var scanned = 0;
        var bytes = 0L;
        var truncated = false;
        DateTimeOffset? previous = null;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned > MaximumScannedEntries) throw Failure("CYGNET_HISTORY_SOURCE_LIMIT");
            if (entry.Value is null || entry.Value.Length is 0 or > MeasurementPayloadLimits.MaximumValueBytes)
                throw Failure("CYGNET_HISTORY_VALUE_INVALID");
            if ((bytes += entry.Value.Length) > HistoricalReadLimits.MaximumSourceBytes)
                throw Failure("CYGNET_HISTORY_SOURCE_LIMIT");
            if (entry.Timestamp == default || entry.Timestamp.Kind == DateTimeKind.Local)
                throw Failure("CYGNET_HISTORY_TIMESTAMP_INVALID");
            // The VHS POC defines timestamps as UTC even when DateTime.Kind is Unspecified.
            // This assumption must be checked on the licensed host; never apply the machine's local time zone.
            var time = new DateTimeOffset(DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc));
            if (previous is { } last && time > last) throw Failure("CYGNET_HISTORY_ORDER_INVALID");
            previous = time;
            if (time < request.StartUtc || time >= request.EndUtc) continue;
            if (samples.Count == request.MaximumSamples) { truncated = true; break; }
            var value = Decode(entry.Kind, entry.Value);
            if (entry.StatusFlags is { Length: > 256 }) throw Failure("CYGNET_HISTORY_STATUS_INVALID");
            var quality = string.IsNullOrWhiteSpace(entry.StatusFlags) ? "Unknown" : CygNetValues.Quality(entry.StatusFlags);
            samples.Add(new(request.Quantity, value, unit, time, quality, "cygnet:history", null, "HistorianSample"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        samples.Reverse();
        var acquired = (retrievedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        for (var index = 0; index < samples.Count; index++) samples[index] = samples[index] with { RetrievedAtUtc = acquired };
        var result = new HistoricalReadResult(request.Quantity, "cygnet:history", sourcePoint, "Raw",
            request.StartUtc.ToUniversalTime(), request.EndUtc.ToUniversalTime(), samples, truncated,
            truncated ? ["SAMPLE_LIMIT_REACHED", "LATEST_SAMPLES_ONLY"] : []);
        if (JsonSerializer.SerializeToUtf8Bytes(result).Length > MeasurementPayloadLimits.MaximumReplyBytes)
            throw Failure("CYGNET_PAYLOAD_TOO_LARGE");
        return result;
    }

    private static CygNetReadException Failure(string code) => new(code, "The CygNet historian returned an unsupported or invalid history payload.");
}
