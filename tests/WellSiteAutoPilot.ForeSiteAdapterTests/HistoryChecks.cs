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
        Check(handler.Path == "/prefix/Surveillance/CygNetTrends/42/2026-11-01T06:00:00Z/2026-11-01T08:00:00Z" &&
            handler.Body == "[\"CurrentPumpFillage\"]", "History route or logical quantity mapping drifted.");
        result = await provider.ReadHistoryAsync(request with { MaximumSamples = 1000 });
        Check(!result.Truncated && result.Samples.Count == 2 && result.Samples[1].TimestampUtc == start.AddHours(1),
            "DST repeated local hours must remain distinct instants, with the exclusive end filtered.");
        // The POC's CurrentSPM trend has its own source UDC and reported unit.
        handler.Payload = """
            [{"ErrorMessage":"Success","PointUDC":"SPSTRN","Units":"SPM","PointValues":[
            {"Timestamp":"2026-11-01T06:00:07.259Z","Value":10.3},
            {"Timestamp":"2026-11-01T06:04:09.945Z","Value":11.4}]}]
            """;
        var strokeRate = await provider.ReadHistoryAsync(request with { Quantity = "SPM", MaximumSamples = 2 });
        Check(handler.Body == "[\"CurrentSPM\"]" && strokeRate.Quantity == "SPM" && strokeRate.SourcePoint == "SPSTRN" &&
            strokeRate.Samples.Count == 2 && strokeRate.Samples[0].Value == 10.3m && strokeRate.Samples[1].Value == 11.4m &&
            strokeRate.Samples.All(sample => sample.Unit == "SPM" && sample.Quality == "Unknown"),
            "Stroke-rate history lost its native quantity mapping, reported unit, quality or measured points.");
        // Any catalogue field reads as history. Elapsed times trend in hours and counts carry no unit on real wells.
        handler.Payload = """
            [{"ErrorMessage":"Success","PointUDC":"IDLE","Units":"h","PointValues":[{"Timestamp":"2026-11-01T06:00:00Z","Value":1.5}]}]
            """;
        var idle = await provider.ReadHistoryAsync(request with { Quantity = "IdleTimeYesterday", MaximumSamples = 2 });
        Check(handler.Body == "[\"IdleTimeYesterday\"]" && idle.Samples[0].Value == 5400m && idle.Samples[0].Unit == "s",
            "Hours were not converted to the catalogue unit.");
        handler.Payload = """
            [{"ErrorMessage":"Success","PointUDC":"CYC","PointValues":[{"Timestamp":"2026-11-01T06:00:00Z","Value":12}]}]
            """;
        var cycles = await provider.ReadHistoryAsync(request with { Quantity = "CyclesToday", MaximumSamples = 2 });
        Check(cycles.Samples[0].Unit == "count" && cycles.Samples[0].Value == 12m, "A unitless count was not read as a count.");
        foreach (var (quantity, unit) in new[] { ("CyclesToday", "kg"), ("CurrentSPM", "Hz"), ("RunTimeToday", "min") })
        {
            handler.Payload = "[{\"ErrorMessage\":\"Success\",\"Units\":\"" + unit + "\",\"PointValues\":[{\"Timestamp\":\"2026-11-01T06:00:00Z\",\"Value\":1}]}]";
            try { await provider.ReadHistoryAsync(request with { Quantity = quantity }); throw new InvalidOperationException("A unit that is not the catalogue's was accepted."); }
            catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_UNIT_MISMATCH") { }
        }
        try { await provider.ReadHistoryAsync(request with { Quantity = "NotAField" }); throw new InvalidOperationException("An unknown field was accepted."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_QUANTITY_UNSUPPORTED") { }
        var keys = ForeSiteAdapter.Descriptor.Operations!.Select(operation => operation.Key).ToHashSet();
        Check(keys.Count(key => key.StartsWith("history.", StringComparison.Ordinal)) == ForeSiteSurveillanceCatalogue.All.Count &&
            keys.Contains("history.pump-fillage") && keys.Contains("history.current-spm"), "Every surveillance field must be readable as history.");
        Check(ForeSiteAdapter.Descriptor.OperationGroups!.All(group => group.Key != "well-trends"), "History must not have its own section.");
        handler.Payload = null;
        foreach (var bad in new[] { request with { EndUtc = start }, request with { EndUtc = start.AddDays(32) },
            request with { MaximumSamples = 0 }, request with { MaximumSamples = 1001 } })
        {
            try { await provider.ReadHistoryAsync(bad); throw new InvalidOperationException("Invalid history bounds accepted."); }
            catch (ArgumentException) { }
        }
        try { await provider.ReadHistoryAsync(request with { StartUtc = start.AddMilliseconds(1) });
            throw new InvalidOperationException("Source bounds were silently rounded."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_HISTORY_PRECISION_UNSUPPORTED") { }
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
