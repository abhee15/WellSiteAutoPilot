using System.Globalization;
using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Integration.ForeSite;

namespace WellSiteAutoPilot.Cli.Diagnostics;

// Operator-only evidence collection. This never changes runtime acceptance policy.
public static class ForeSiteMeasurementProbe
{
    private static readonly string[] NumericFields = ["CurrentPumpFillage", "AveragePumpFillage"];
    private static readonly string[] TimeFields = ["LastScanTime", "LastGoodScanTime", "MeasurementTime", "MeasurementTimestamp",
        "ValueTimestamp", "Timestamp", "LastUpdateTime", "CurrentPumpFillageTimestamp", "AveragePumpFillageTimestamp"];
    private static readonly string[] QualityFields = ["Quality", "DataQuality", "QualityCode", "Status", "StatusCode",
        "CurrentPumpFillageQuality", "AveragePumpFillageQuality"];
    private static readonly string[] UnitFields = ["Unit", "Units", "CurrentPumpFillageUnit", "AveragePumpFillageUnit"];
    private static readonly string[] QualityLabels = ["Good", "Bad", "Uncertain", "Unknown", "Invalid", "Stale", "NotAvailable"];
    private static readonly string[] UnitLabels = ["%", "Percent", "Percentage", "Fraction"];
    private static readonly string[] OffsetFormats = ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];
    private static readonly string[] LocalFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"];
    private static JsonSerializerOptions ReportJsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Validate(long wellId, int samples, int intervalSeconds)
    {
        if (wellId <= 0 || samples is < 2 or > 12 || intervalSeconds is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(samples), "Invalid bounded probe parameters.");
    }

    public static async Task<ForeSiteProbeReport> CaptureAsync(HttpClient client, long wellId, int samples = 3,
        int intervalSeconds = 5, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        Validate(wellId, samples, intervalSeconds);
        // The caller must supply a fresh client; the probe owns its buffering policy.
        client.MaxResponseContentBufferSize = 1024 * 1024;
        var session = new ForeSiteSession(client);
        var observations = new List<ForeSiteProbeSample>();
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(180));
        try
        {
            for (var index = 0; index < samples; index++)
            {
                if (index > 0) await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), total.Token);
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                operation.CancelAfter(TimeSpan.FromSeconds(10));
                observations.Add(await CaptureOneAsync(session, wellId, index + 1, operation.Token, total.Token));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && total.IsCancellationRequested)
        {
            return new(1, Guid.NewGuid(), "Unverified", false, "PARTIAL_TOTAL_TIMEOUT", samples, intervalSeconds, observations);
        }
        return new(1, Guid.NewGuid(), "Unverified", false, "CAPTURED", samples, intervalSeconds, observations);
    }

    private static async Task<ForeSiteProbeSample> CaptureOneAsync(ForeSiteSession session, long wellId, int sequence,
        CancellationToken operation, CancellationToken total)
    {
        var started = DateTimeOffset.UtcNow;
        int? status = null;
        try
        {
            using var response = await session.GetAsync("Surveillance/well/" + wellId.ToString(CultureInfo.InvariantCulture), operation);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) return Failure("FORESITE_PROBE_HTTP_ERROR");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(operation),
                new JsonDocumentOptions { MaxDepth = 16 }, operation);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failure("FORESITE_PROBE_RESPONSE_INVALID");
            if (root.TryGetProperty("FullErrorMessage", out var error) && error.ValueKind != JsonValueKind.Null &&
                (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString())))
                return Failure("FORESITE_PROBE_PROVIDER_ERROR");
            if (!root.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Object)
                return Failure("FORESITE_PROBE_RESPONSE_INVALID");
            var fields = new Dictionary<string, ForeSiteProbeField>(StringComparer.Ordinal);
            foreach (var name in NumericFields) fields[name] = ReadField(value, name, "number");
            foreach (var name in TimeFields) fields[name] = ReadField(value, name, "time");
            foreach (var name in QualityFields) fields[name] = ReadField(value, name, "quality");
            foreach (var name in UnitFields) fields[name] = ReadField(value, name, "unit");
            return new(sequence, started, DateTimeOffset.UtcNow, "OBSERVED", status, fields);
        }
        catch (ForeSiteTokenException) { return Failure("FORESITE_PROBE_AUTHENTICATION_FAILED"); }
        catch (JsonException) { return Failure("FORESITE_PROBE_RESPONSE_INVALID"); }
        catch (HttpRequestException) { return Failure("FORESITE_PROBE_TRANSPORT_FAILED"); }
        catch (OperationCanceledException) when (!total.IsCancellationRequested) { return Failure("FORESITE_PROBE_READ_TIMEOUT"); }

        ForeSiteProbeSample Failure(string code) => new(sequence, started, DateTimeOffset.UtcNow, code, status,
            new Dictionary<string, ForeSiteProbeField>());
    }

    private static ForeSiteProbeField ReadField(JsonElement value, string name, string kind)
    {
        if (!value.TryGetProperty(name, out var field)) return new("Missing");
        if (field.ValueKind == JsonValueKind.Null) return new("Null");
        if (kind == "number") return field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var number)
            ? new(number is < 0 or > 100 ? "OutsideCurrentRuntimeRange" : "Numeric", Number: number)
            : new("InvalidType");
        if (kind == "time")
        {
            if (field.ValueKind != JsonValueKind.String) return new("InvalidType");
            var text = field.GetString();
            if (text is { Length: <= 40 } && DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var offset))
                return new("TimestampWithOffset", TimestampUtc: offset.ToUniversalTime(), OffsetMinutes: (int)offset.Offset.TotalMinutes);
            if (text is { Length: <= 40 } && DateTime.TryParseExact(text, LocalFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
                return new("TimestampWithoutOffset", LocalTime: local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
            return new("InvalidTimestamp");
        }
        if (kind == "quality" && field.ValueKind == JsonValueKind.Number)
            return field.TryGetInt32(out var code) && code is >= 0 and <= 65535 ? new("NumericCode", Number: code) : new("InvalidCode");
        if (field.ValueKind != JsonValueKind.String) return new("InvalidType");
        var label = (kind == "unit" ? UnitLabels : QualityLabels).FirstOrDefault(label =>
            string.Equals(label, field.GetString(), StringComparison.OrdinalIgnoreCase));
        return label is null ? new("UnrecognizedTextExcluded") : new("RecognizedLabel", Label: label);
    }

    public static async Task WriteNewReportAsync(string path, ForeSiteProbeReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var full = Path.GetFullPath(path);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(stream, report, ReportJsonOptions, cancellationToken);
            File.Move(temporary, full, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed record ForeSiteProbeField(string State, decimal? Number = null, DateTimeOffset? TimestampUtc = null,
    int? OffsetMinutes = null, string? Label = null, string? LocalTime = null);
public sealed record ForeSiteProbeSample(int Sequence, DateTimeOffset RequestStartedAtUtc, DateTimeOffset RetrievedAtUtc,
    string Outcome, int? HttpStatus, IReadOnlyDictionary<string, ForeSiteProbeField> Fields);
public sealed record ForeSiteProbeReport(int SchemaVersion, Guid ProbeId, string MeasurementSemantics,
    bool AuthorizesExecution, string CaptureOutcome, int RequestedSamples, int IntervalSeconds,
    IReadOnlyCollection<ForeSiteProbeSample> Samples);

public static class ForeSiteProbeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 4 or > 6 || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var well) ||
            !int.TryParse(args.Length > 4 ? args[4] : "3", NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            !int.TryParse(args.Length > 5 ? args[5] : "5", NumberStyles.None, CultureInfo.InvariantCulture, out var interval))
            return Fail("FORESITE_PROBE_ARGUMENTS_INVALID");
        try { ForeSiteMeasurementProbe.Validate(well, count, interval); }
        catch (ArgumentOutOfRangeException) { return Fail("FORESITE_PROBE_ARGUMENTS_INVALID"); }
        if (!Uri.TryCreate(Environment.GetEnvironmentVariable("FORESITE_BASE_URL"), UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0)
            return Fail("FORESITE_PROBE_ADDRESS_INVALID");
        if (!OperatingSystem.IsWindows()) return Fail("FORESITE_PROBE_WINDOWS_REQUIRED");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var output = Path.GetFullPath(args[3]);
            if (!Path.GetExtension(output).Equals(".json", StringComparison.OrdinalIgnoreCase))
                return Fail("FORESITE_PROBE_OUTPUT_INVALID");
            if (File.Exists(output) || Directory.Exists(output)) return Fail("FORESITE_PROBE_OUTPUT_EXISTS");
            // Local diagnostic identity is the current process, never module-supplied credentials.
            using var handler = new HttpClientHandler { UseDefaultCredentials = true, UseCookies = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = ForeSiteAdapter.BaseAddress(address.AbsoluteUri), Timeout = Timeout.InfiniteTimeSpan };
            var report = await ForeSiteMeasurementProbe.CaptureAsync(client, well, count, interval, cancellation.Token);
            await ForeSiteMeasurementProbe.WriteNewReportAsync(output, report, cancellation.Token);
            Console.WriteLine("Sanitized probe report saved. Measurement semantics remain unverified.");
            return report.CaptureOutcome == "CAPTURED" && report.Samples.All(sample => sample.Outcome == "OBSERVED") ? 0 : 2;
        }
        catch (OperationCanceledException) { return Fail("FORESITE_PROBE_CANCELLED", 130); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Fail("FORESITE_PROBE_OUTPUT_OR_CONFIGURATION_INVALID"); }
        finally { Console.CancelKeyPress -= cancel; }
    }
    private static int Fail(string code, int exitCode = 2) { Console.Error.WriteLine(code); return exitCode; }
}
