using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.IntegrationGateway.Providers;

internal static class MeasurementChecks
{
    public static async Task RunAsync()
    {
        var factory = new Factory();
        using var pool = new ForeSiteConnectionMeasurements(factory, TimeProvider.System, TimeSpan.Zero);
        var a = new ConnectionMeasurementRequest(Guid.NewGuid(), 1, new() { ["baseUrl"] = "http://a/prefix" }, "42", "PumpFillage");
        var b = a with { ConnectionId = Guid.NewGuid(), Settings = new() { ["baseUrl"] = "http://b/prefix" } };
        var first = await pool.ReadAsync(a, CancellationToken.None);
        var second = await pool.ReadAsync(b, CancellationToken.None);
        Check(first.Value == 80 && second.Value == 60 && factory.Tokens == 2, "two connection sessions crossed servers or did not authenticate");
        Check(first.Quality == "Good" && first.TimestampKind == "ControllerScan" && first.RetrievedAtUtc is not null &&
            first.TimestampUtc == new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero), "successful controller acquisition lost its UTC time or provenance");
        factory.Expire = true;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => pool.ReadAsync(a, CancellationToken.None)));
        Check(factory.Tokens == 3, "concurrent expiry caused repeated token creation");
        await pool.ReadAsync(a with { ConnectionRevision = 2 }, CancellationToken.None);
        Check(factory.Tokens == 4, "a changed connection revision reused its old session");
        Check(factory.Paths.All(p => p is "/prefix/TokenService/CreateToken" or "/prefix/Surveillance/well/42"), "virtual directory or quantity path escaped");
        Check(factory.Methods.All(m => m == HttpMethod.Get), "measurement acquisition made a provider write");
        foreach (var (json, code) in new[] {
            ("<html>login</html>", "FORESITE_RESPONSE_INVALID"),
            ("{\"Value\":{}}", "FORESITE_MEASUREMENT_MISSING"),
            ("{\"Value\":{\"CurrentPumpFillage\":null}}", "FORESITE_MEASUREMENT_MISSING"),
            ("{\"Value\":{\"CurrentPumpFillage\":\"NaN\"}}", "FORESITE_MEASUREMENT_INVALID"),
            ("{\"Value\":{\"CurrentPumpFillage\":101}}", "FORESITE_MEASUREMENT_INVALID"),
            ("{\"Value\":{\"CurrentPumpFillage\":80,\"LastGoodScanTime\":\"2026-10-27T05:00:00\"}}", "FORESITE_TIMESTAMP_INVALID") })
        {
            factory.Payload = json;
            try { await pool.ReadAsync(a with { ConnectionRevision = 2 }, CancellationToken.None); }
            catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException e) when (e.Code == code) { continue; }
            throw new InvalidOperationException("Measurement parser accepted " + code);
        }
        // Latest scan attempt/status must not replace the last successful acquisition.
        factory.Payload = """
            {"Value":{"CurrentPumpFillage":0,"AveragePumpFillage":100,
              "LastGoodScanTime":"2026-10-27T00:00:00-05:00",
              "LastScanTime":"2026-10-27T05:30:00Z","CommStatus":"Failed",
              "LatestValidWellTestDate":"2022-09-30T18:30:00Z",
              "WellTest_WaterCut":50,"PointUDC":"SITE-SPECIFIC-IGNORED"}}
            """;
        var current = await pool.ReadAsync(a, CancellationToken.None);
        var average = await pool.ReadAsync(a with { Quantity = "AveragePumpFillage" }, CancellationToken.None);
        Check(current.Value == 0 && average.Value == 100 && current.Unit == "%" && average.Unit == "%" &&
            current.TimestampUtc == first.TimestampUtc && average.TimestampUtc == first.TimestampUtc &&
            current.Quality == "Good" && average.Quality == "Good",
            "latest failed scan, test date or site UDC changed a successful controller acquisition");
        foreach (var timestamp in new[] { "null", "123", "\"invalid\"", "\"0001-01-01T00:00:00Z\"" })
        {
            factory.Payload = "{\"Value\":{\"CurrentPumpFillage\":80,\"LastScanTime\":\"2026-10-27T05:30:00Z\",\"LastGoodScanTime\":" + timestamp + "}}";
            await RejectAsync(a, "FORESITE_TIMESTAMP_INVALID");
        }
        factory.Payload = """{"Value":{"CurrentPumpFillage":80,"LastScanTime":"2026-10-27T05:30:00Z"}}""";
        await RejectAsync(a, "FORESITE_TIMESTAMP_INVALID");
        // An unsupported/test-derived field never inherits a controller scan timestamp.
        await RejectAsync(a with { Quantity = "WaterCut" }, "FORESITE_QUANTITY_UNSUPPORTED");
        async Task RejectAsync(ConnectionMeasurementRequest request, string code)
        {
            try { await pool.ReadAsync(request, CancellationToken.None); }
            catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException e) when (e.Code == code) { return; }
            throw new InvalidOperationException("Measurement parser accepted " + code);
        }
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try { await pool.ReadAsync(a, cancellation.Token); throw new InvalidOperationException("Caller cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        // G2: the unit and range come from ForeSite's Units block when it sends one.
        factory.Payload = """{"Value":{"CurrentPumpFillage":0.8,"LastGoodScanTime":"2026-10-27T00:00:00Z"},"Units":{"CurrentPumpFillage":{"UnitKey":"fraction","Min":0,"Max":1}}}""";
        var provided = await pool.ReadAsync(a, CancellationToken.None);
        Check(provided.Value == 0.8m && provided.Unit == "fraction", "the adapter replaced the unit ForeSite reported");
        factory.Payload = """{"Value":{"CurrentPumpFillage":1.5,"LastGoodScanTime":"2026-10-27T00:00:00Z"},"Units":{"CurrentPumpFillage":{"UnitKey":"fraction","Min":0,"Max":1}}}""";
        await RejectAsync(a, "FORESITE_MEASUREMENT_INVALID");
        factory.Payload = null;
        // G1: further status quantities come from the same document, with ForeSite's own units.
        factory.Payload = """
            {"Value":{"CurrentSPM":12.3,"CyclesYesterday":0,"RunTimeYesterday":24540,"LastGoodScanTime":"2026-10-27T00:00:00Z"},
             "Units":{"CurrentSPM":{"UnitKey":"SPM","Precision":1},"RunTimeYesterday":{"UnitKey":"s","Precision":2}}}
            """;
        var spm = await pool.ReadAsync(a with { Quantity = "CurrentSPM" }, CancellationToken.None);
        var cycles = await pool.ReadAsync(a with { Quantity = "CyclesYesterday" }, CancellationToken.None);
        var runtime = await pool.ReadAsync(a with { Quantity = "RunTimeYesterday" }, CancellationToken.None);
        Check(spm is { Value: 12.3m, Unit: "SPM" } && cycles is { Value: 0, Unit: "" } && runtime is { Value: 24540, Unit: "s" },
            "added status quantities lost their value or ForeSite unit; a real zero must stay a value");
        factory.Payload = null;
        // G9: quantities asked for together are served from one status document.
        var fanOut = new Factory();
        using (var cached = new ForeSiteConnectionMeasurements(fanOut, TimeProvider.System, TimeSpan.FromSeconds(30)))
        {
            fanOut.Payload = """{"Value":{"CurrentPumpFillage":70,"AveragePumpFillage":65,"LastGoodScanTime":"2026-10-27T00:00:00-05:00"}}""";
            var now = await cached.ReadAsync(a, CancellationToken.None);
            var avg = await cached.ReadAsync(a with { Quantity = "AveragePumpFillage" }, CancellationToken.None);
            Check(now.Value == 70 && avg.Value == 65 && fanOut.Paths.Count(p => p.EndsWith("/Surveillance/well/42", StringComparison.Ordinal)) == 1,
                "two quantities of one well did not share one status read");
            await cached.ReadAsync(a with { ConnectionId = Guid.NewGuid() }, CancellationToken.None);
            Check(fanOut.Paths.Count(p => p.EndsWith("/Surveillance/well/42", StringComparison.Ordinal)) == 2,
                "a different connection reused another connection's cached status");
        }
        Console.WriteLine("ForeSite measurement sessions: connection isolation, prefix, cookies, concurrent expiry, revision, zero writes, strict values/timestamps, controller acquisition provenance and single status read checks passed.");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private sealed class Factory : IHttpClientFactory
    {
        public int Tokens { get; set; }
        public bool Expire { get; set; }
        public string? Payload { get; set; }
        public List<string> Paths { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public HttpClient CreateClient(string name) => new(new Handler(this));
    }
    private sealed class Handler(Factory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            factory.Paths.Add(request.RequestUri!.AbsolutePath);
            factory.Methods.Add(request.Method);
            if (request.RequestUri.AbsolutePath.EndsWith("CreateToken", StringComparison.Ordinal))
            {
                factory.Tokens++;
                var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                token.Headers.TryAddWithoutValidation("Set-Cookie", "fs=" + request.RequestUri.Host + "; Path=/prefix");
                return Task.FromResult(token);
            }
            if (!request.Headers.TryGetValues("Cookie", out var cookies) || !cookies.Single().Contains("fs=" + request.RequestUri.Host, StringComparison.Ordinal))
                throw new InvalidOperationException("Cookies crossed a connection or were omitted.");
            if (factory.Expire)
            {
                factory.Expire = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            var number = request.RequestUri.Host == "a" ? 80 : 60;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(factory.Payload ??
                "{\"Value\":{\"CurrentPumpFillage\":" + number + ",\"LastScanTime\":\"2026-10-27T00:00:00-05:00\",\"LastGoodScanTime\":\"2026-10-27T00:00:00-05:00\"}}", Encoding.UTF8, "application/json") });
        }
    }
}
