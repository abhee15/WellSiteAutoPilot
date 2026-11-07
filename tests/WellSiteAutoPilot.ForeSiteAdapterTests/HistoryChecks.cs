using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.IntegrationGateway.Providers;

internal static class HistoryChecks
{
    public static async Task RunAsync()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://foresite.test/prefix/") };
        var provider = new ForeSiteHistoricalDataProvider(new(http));
        var start = DateTimeOffset.Parse("2026-11-01T01:00:00-05:00", System.Globalization.CultureInfo.InvariantCulture);
        var request = new HistoricalReadRequest("42", "PumpFillage", start, start.AddHours(2), 1);
        var result = await provider.ReadHistoryAsync(request);
        Check(result.Truncated && result.Samples.Count == 1 && result.Samples[0].TimestampUtc == start &&
            result.Samples[0].Value == 80 && result.Samples[0].Quality == "Unknown" && result.Samples[0].Unit == "%" &&
            result.SourcePoint == "RRLPFILCUR" && result.Aggregation == "Raw", "History lost source semantics, ordering or truncation.");
        Check(handler.Path!.Contains("/prefix/Surveillance/CygNetTrends/42/2026-11-01T06:00:00", StringComparison.Ordinal) &&
            handler.Body == "[\"CurrentPumpFillage\"]", "History route or logical quantity mapping drifted.");
        result = await provider.ReadHistoryAsync(request with { MaximumSamples = 1000 });
        Check(!result.Truncated && result.Samples.Count == 2 && result.Samples[1].TimestampUtc == start.AddHours(1),
            "DST repeated local hours must remain distinct instants, with the exclusive end filtered.");
        foreach (var bad in new[] { request with { EndUtc = start }, request with { EndUtc = start.AddDays(32) },
            request with { MaximumSamples = 0 }, request with { MaximumSamples = 1001 } })
        {
            try { await provider.ReadHistoryAsync(bad); throw new InvalidOperationException("Invalid history bounds accepted."); }
            catch (ArgumentException) { }
        }
        foreach (var (payload, code) in new[] {
            ("[]", "FORESITE_RESPONSE_INVALID"),
            ("[{\"ErrorMessage\":\"Success\",\"ErrorMessage\":\"Success\"}]", "FORESITE_RESPONSE_INVALID"),
            ("[{\"ErrorMessage\":\"unknown SECRET\"}]", "FORESITE_TREND_FAILED"),
            ("[{\"ErrorMessage\":\"Success\",\"Units\":\"%\",\"PointValues\":[{\"Timestamp\":\"2026-11-01T01:00:00\",\"Value\":80}]}]", "FORESITE_TIMESTAMP_INVALID"),
            ("[{\"ErrorMessage\":\"Success\",\"PointValues\":[]}]", "FORESITE_UNIT_MISSING"),
            ("<html>login</html>", "FORESITE_RESPONSE_INVALID") })
        {
            handler.Payload = payload;
            try { await provider.ReadHistoryAsync(request); throw new InvalidOperationException("Invalid history payload accepted."); }
            catch (ForeSiteMeasurementException error) when (error.Code == code && !error.Message.Contains("SECRET", StringComparison.Ordinal)) { }
        }
        handler.Payload = new string(' ', HistoricalReadLimits.MaximumSourceBytes + 1);
        handler.Chunked = true;
        try { await provider.ReadHistoryAsync(request); throw new InvalidOperationException("Unbounded chunked source accepted."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_PAYLOAD_TOO_LARGE") { }
        handler.Chunked = false;
        handler.Payload = null;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        try { await provider.ReadHistoryAsync(request, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("ForeSite history: bounds, UTC/DST, source units, unknown quality, strict errors, truncation and cancellation passed (recorded-shape fixtures).");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Chunked { get; set; }
        public string? Payload { get; set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("CreateToken", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            if (Chunked) return new(HttpStatusCode.OK) { Content = new ChunkedContent(Payload!) };
            Path = request.RequestUri.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(Payload ?? """
                [{"ErrorMessage":"Success","Units":"%","PointUDC":"RRLPFILCUR","PointValues":[
                {"Timestamp":"2026-11-01T01:00:00-06:00","Value":70},
                {"Timestamp":"2026-11-01T01:00:00-05:00","Value":80},
                {"Timestamp":"2026-11-01T08:00:00Z","Value":99}]}]
                """, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ChunkedContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }

}
