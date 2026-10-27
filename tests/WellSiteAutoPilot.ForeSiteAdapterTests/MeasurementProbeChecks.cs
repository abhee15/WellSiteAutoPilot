using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Cli.Diagnostics;

internal static class MeasurementProbeChecks
{
    public static async Task RunAsync()
    {
        foreach (var (well, count, interval) in new[] { (0L, 3, 5), (42L, 1, 5), (42L, 13, 5), (42L, 3, 0), (42L, 3, 31) })
        {
            try { ForeSiteMeasurementProbe.Validate(well, count, interval); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Probe admitted an unbounded/invalid acquisition.");
        }
        var fixture = new RecordedHttp();
        using var http = Client(fixture);
        var report = await ForeSiteMeasurementProbe.CaptureAsync(http, 42, 3, 1);
        Check(report.SchemaVersion == 1 && report.CaptureOutcome == "CAPTURED" && !report.AuthorizesExecution &&
            report.MeasurementSemantics == "Unverified" && report.Samples.Count == 3, "Evidence became execution authority.");
        var samples = report.Samples.ToArray();
        Check(fixture.Tokens == 2 && fixture.Reads == 4, "The probe duplicated authentication or failed to retry exactly one 401.");
        Check(samples.All(s => s.Outcome == "OBSERVED" && s.HttpStatus == 200 && s.RetrievedAtUtc >= s.RequestStartedAtUtc),
            "Probe lost observation status/capture timing.");
        Check(samples[0].Fields["CurrentPumpFillage"].Number == 99.6m &&
            samples[1].Fields["CurrentPumpFillage"].Number == 99.6m &&
            samples[0].Fields["LastScanTime"].TimestampUtc != samples[1].Fields["LastScanTime"].TimestampUtc,
            "Stable values and advancing scan times were conflated.");
        var fields = samples[0].Fields;
        Check(fields["LastGoodScanTime"].OffsetMinutes == -300 &&
            fields["LastGoodScanTime"].TimestampUtc == new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero) &&
            fields["MeasurementTime"].State == "TimestampWithoutOffset" && fields["MeasurementTime"].TimestampUtc is null &&
            fields["MeasurementTimestamp"].State == "InvalidTimestamp" && fields["Timestamp"].State == "Missing",
            "The probe inferred timezone or lost explicit/invalid timestamp facts.");
        Check(fields["AveragePumpFillage"].State == "Null" && fields["Quality"].Label == "Good" &&
            fields["DataQuality"].State == "UnrecognizedTextExcluded" && fields["QualityCode"].Number == 192 &&
            fields["Unit"].Label == "%" && fields["Units"].State == "UnrecognizedTextExcluded" &&
            samples[2].Fields["CurrentPumpFillage"].State == "OutsideCurrentRuntimeRange" &&
            samples[2].Fields["AveragePumpFillage"].State == "InvalidType",
            "The probe invented units/quality/missing values or hid range facts.");
        var folder = Path.Combine(Path.GetTempPath(), "wsa-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "report.json");
            await ForeSiteMeasurementProbe.WriteNewReportAsync(path, report);
            var json = await File.ReadAllTextAsync(path);
            Check(!json.Contains("private", StringComparison.OrdinalIgnoreCase) && !json.Contains("customer", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("Cookie", StringComparison.OrdinalIgnoreCase) && !json.Contains("FullErrorMessage", StringComparison.Ordinal),
                "A report exported non-allowlisted fields or upstream text.");
            using var parsed = JsonDocument.Parse(json);
            Check(parsed.RootElement.GetProperty("samples").GetArrayLength() == 3, "Sanitized report did not round-trip.");
            var refused = false;
            try { await ForeSiteMeasurementProbe.WriteNewReportAsync(path, report); }
            catch (IOException) { refused = true; }
            Check(refused && await File.ReadAllTextAsync(path) == json && Directory.GetFiles(folder).Length == 1,
                "An existing report was overwritten or temporary files leaked.");
        }
        finally { Directory.Delete(folder, recursive: true); }

        foreach (var (status, json, expected) in new[]
        {
            (HttpStatusCode.BadGateway, "private gateway error", "FORESITE_PROBE_HTTP_ERROR"),
            (HttpStatusCode.Found, "private redirect", "FORESITE_PROBE_HTTP_ERROR"),
            (HttpStatusCode.OK, "<html>private login</html>", "FORESITE_PROBE_RESPONSE_INVALID"),
            (HttpStatusCode.OK, "{\"FullErrorMessage\":\"private server error\",\"Value\":{}}", "FORESITE_PROBE_PROVIDER_ERROR"),
            (HttpStatusCode.OK, "{\"Value\":[]}", "FORESITE_PROBE_RESPONSE_INVALID"),
            (HttpStatusCode.OK, new string('[', 20) + "0" + new string(']', 20), "FORESITE_PROBE_RESPONSE_INVALID"),
            (HttpStatusCode.OK, new string('x', 1024 * 1024 + 1), "FORESITE_PROBE_TRANSPORT_FAILED")
        })
        {
            using var failed = Client(new FixedResponse(status, json));
            var failures = await ForeSiteMeasurementProbe.CaptureAsync(failed, 42, 2, 1);
            Check(failures.Samples.All(s => s.Outcome == expected && s.Fields.Count == 0), "Probe failed to classify " + expected);
        }
        using var brokenToken = Client(new FixedResponse(HttpStatusCode.Unauthorized, "private", failToken: true));
        var denied = await ForeSiteMeasurementProbe.CaptureAsync(brokenToken, 42, 2, 1);
        Check(denied.Samples.All(s => s.Outcome == "FORESITE_PROBE_AUTHENTICATION_FAILED"), "Token errors escaped the safe boundary.");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var cancelledHttp = Client(new RecordedHttp());
        try { await ForeSiteMeasurementProbe.CaptureAsync(cancelledHttp, 42, 2, 1, cancelled.Token); }
        catch (OperationCanceledException) { Console.WriteLine("ForeSite probe: bounded GET/session flow, sanitized evidence, offset/missing/range facts, failures, cancellation and no-overwrite checks passed."); return; }
        throw new InvalidOperationException("Probe ignored caller cancellation.");
    }
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://private-fixture/prefix/") };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class RecordedHttp : HttpMessageHandler
    {
        public int Tokens { get; private set; }
        public int Reads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "private-fixture", "Probe wrote or crossed source addresses.");
            if (request.RequestUri!.AbsolutePath == "/prefix/TokenService/CreateToken")
            {
                Tokens++;
                var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                token.Headers.TryAddWithoutValidation("Set-Cookie", $"fs=private{Tokens}; Path=/prefix");
                return Task.FromResult(token);
            }
            Check(request.RequestUri.AbsolutePath == "/prefix/Surveillance/well/42" &&
                request.Headers.TryGetValues("Cookie", out var cookies) && cookies.Single() == $"fs=private{Tokens}",
                "Probe lost the numeric source, virtual path or shared session cookie.");
            Reads++;
            if (Reads == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                Value = new { CurrentPumpFillage = Reads == 4 ? 101m : 99.6m, AveragePumpFillage = Reads == 4 ? (object)"private" : null,
                    LastScanTime = $"2026-10-27T00:00:0{Reads}-05:00", LastGoodScanTime = "2026-10-27T00:00:00-05:00",
                    MeasurementTime = "2026-10-27T00:00:00", MeasurementTimestamp = "private-secret",
                    Quality = "Good", DataQuality = "private-secret", QualityCode = 192, Unit = "%", Units = "customer-unit",
                    WellName = "customer-private", Password = "private-password", Cookie = "private-cookie" },
                CustomerName = "private-customer"
            })) });
        }
    }
    private sealed class FixedResponse(HttpStatusCode status, string json, bool failToken = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(request.Method == HttpMethod.Get, "Probe made a write.");
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("CreateToken", StringComparison.Ordinal) && !failToken
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
                : new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
