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
        using var pool = new ForeSiteConnectionMeasurements(factory, TimeProvider.System);
        var a = new ConnectionMeasurementRequest(Guid.NewGuid(), 1, new() { ["baseUrl"] = "http://a/prefix" }, "42", "PumpFillage");
        var b = a with { ConnectionId = Guid.NewGuid(), Settings = new() { ["baseUrl"] = "http://b/prefix" } };
        var first = await pool.ReadAsync(a, CancellationToken.None);
        var second = await pool.ReadAsync(b, CancellationToken.None);
        Check(first.Value == 80 && second.Value == 60 && factory.Tokens == 2, "two connection sessions crossed servers or did not authenticate");
        Check(first.Quality == "Unknown" && first.TimestampKind == "UnverifiedScan" && first.RetrievedAtUtc is not null &&
            first.TimestampUtc == new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero), "scan evidence became verified measurement quality/time");
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
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try { await pool.ReadAsync(a, cancellation.Token); throw new InvalidOperationException("Caller cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("ForeSite measurement sessions: connection isolation, prefix, cookies, concurrent expiry, revision, zero writes, strict values/timestamps and unverified provenance checks passed.");
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
