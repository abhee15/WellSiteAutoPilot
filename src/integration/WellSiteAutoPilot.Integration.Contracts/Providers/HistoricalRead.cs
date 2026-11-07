namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>UTC instant interval [start, end). No local calendar-day or implicit aggregation semantics.</summary>
public sealed record HistoricalReadRequest(string AssetExternalId, string Quantity, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, int MaximumSamples = HistoricalReadLimits.MaximumSamples);

public sealed record HistoricalReadResult(string Quantity, string Source, string? SourcePoint, string Aggregation,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyList<EngineeringValue> Samples, bool Truncated,
    IReadOnlyList<string> Diagnostics);

/// <summary>Rich bounded history extends the existing typed interface without discarding truncation or provenance.</summary>
public interface IBoundedHistoricalDataProvider : IHistoricalDataProvider
{
    ValueTask<HistoricalReadResult> ReadHistoryAsync(HistoricalReadRequest request, CancellationToken cancellationToken = default);
}

public sealed record ConnectionHistoryRequest(Guid ConnectionId, int ConnectionRevision,
    Dictionary<string, string?> Settings, HistoricalReadRequest Read);

public static class HistoricalReadLimits
{
    public const int MaximumWindowDays = 31;
    public const int MaximumSamples = 1000;
    public const int MaximumSourceBytes = 1048576;

    public static void Validate(HistoricalReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AssetExternalId) || request.AssetExternalId.Length > 128 ||
            request.AssetExternalId.Any(char.IsControl) || string.IsNullOrWhiteSpace(request.Quantity) || request.Quantity.Length > 64 ||
            request.StartUtc == default || request.EndUtc <= request.StartUtc ||
            request.EndUtc - request.StartUtc > TimeSpan.FromDays(MaximumWindowDays) ||
            request.MaximumSamples is < 1 or > MaximumSamples)
            throw new ArgumentException("History requires an object, quantity, ordered interval of at most 31 days and 1–1000 samples.", nameof(request));
    }

    public static AdapterOperationDescriptor Operation(string key, string quantity, string resourceKind, int timeoutSeconds,
        string timestampSemantics, string qualitySemantics) => new(
        key, quantity + " history", "Bounded raw history over UTC instants. No resampling or calendar-day aggregation.",
        ProviderCapabilityKind.HistoricalData, "engineering.history", "1.0",
        new("engineering.history-request/v1", [
            new("assetExternalId", "string", true, "Imported source object belonging to the selected connection."),
            new("quantity", "string", true, "Declared logical quantity."),
            new("startUtc", "date-time", true, "Inclusive instant with explicit UTC offset."),
            new("endUtc", "date-time", true, "Exclusive instant with explicit UTC offset."),
            new("maximumSamples", "integer", false, "From 1 to 1000; defaults to 1000.")]),
        new("engineering.history-result/v1", [
            new("quantity", "string", true, "Logical quantity."),
            new("startUtc", "date-time", true, "Inclusive requested instant normalized to UTC."),
            new("endUtc", "date-time", true, "Exclusive requested instant normalized to UTC."),
            new("samples", "engineering.value/v1[]", true, "Ascending source times in [startUtc, endUtc)."),
            new("source", "string", true, "Provider provenance, with simulated sources identified."),
            new("sourcePoint", "string|null", false, "Mapped source point when supplied by provider."),
            new("aggregation", "string", true, "Raw; no implicit averages."),
            new("truncated", "boolean", true, "True when samples were bounded; never treat as complete history."),
            new("diagnostics", "string[]", true, "Stable codes only; no raw provider errors.")]),
        timestampSemantics, qualitySemantics,
        new(1, MeasurementPayloadLimits.MaximumValueBytes, MeasurementPayloadLimits.MaximumReplyBytes, timeoutSeconds,
            MaximumWindowDays, MaximumSamples), quantity, resourceKind);
}
