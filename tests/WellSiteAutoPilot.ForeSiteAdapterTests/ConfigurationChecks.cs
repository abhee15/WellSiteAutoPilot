using System.Net;
using System.Text;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.IntegrationGateway.Providers;

internal static class ConfigurationChecks
{
    public static async Task RunAsync()
    {
        var factory = new Factory();
        using var pool = new ForeSiteConnectionMeasurements(factory, TimeProvider.System);
        var request = new ConnectionConfigurationRequest(Guid.NewGuid(), 1, new() { ["baseUrl"] = "http://foresite.test/prefix" }, new("42"));
        var result = await pool.ReadConfigurationAsync(request, CancellationToken.None);
        Check(result.AssetExternalId == "42" && result.DisplayName == "Example" && result.Details.ClassificationCode == "1" &&
            result.Details.FacilityId == "7" && result.Details.DeviceType is null &&
            result.SourceChangedAtUtc == new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero) &&
            result.RetrievedAtUtc > result.SourceChangedAtUtc && result.Source == "foresite:well-details", "Configuration semantics changed");
        await pool.ReadAsync(new(request.ConnectionId, 1, request.Settings, "42", "PumpFillage"), CancellationToken.None);
        Check(factory.Tokens == 1 && factory.ConfigReads == 1, "Configuration did not share the existing authenticated session");
        factory.Expire = true;
        await pool.ReadConfigurationAsync(request, CancellationToken.None);
        Check(factory.Tokens == 2, "401 did not refresh and replay the exact single-well request");
        await pool.ReadConfigurationAsync(request with { ConnectionRevision = 2 }, CancellationToken.None);
        Check(factory.Tokens == 3, "Changed Connection revision reused the old session");
        factory.Payload = """{"Id":42,"Name":"Example"}""";
        result = await pool.ReadConfigurationAsync(request, CancellationToken.None);
        Check(result.SourceChangedAtUtc is null && result.Details.ClassificationCode is null, "Missing source facts were invented");
        foreach (var (payload, code) in new[] {
            ("{\"Id\":43,\"Name\":\"SECRET\"}", "FORESITE_CONFIGURATION_IDENTITY_MISMATCH"),
            ("{\"Id\":\"42\",\"Name\":\"Example\"}", "FORESITE_CONFIGURATION_IDENTITY_MISMATCH"),
            ("{\"Id\":42,\"Id\":42,\"Name\":\"Example\"}", "FORESITE_CONFIGURATION_INVALID"),
            ("{\"Id\":42,\"Name\":\"Example\",\"name\":\"Other\"}", "FORESITE_CONFIGURATION_INVALID"),
            ("{\"Id\":42,\"Name\":\"Example\",\"ChangeDate\":\"2026-11-01T01:00:00\"}", "FORESITE_CONFIGURATION_TIMESTAMP_INVALID"),
            ("{\"Id\":42,\"Name\":\"Example\",\"WellType\":{}}", "FORESITE_CONFIGURATION_INVALID"),
            ("{\"Id\":42,\"Name\":\"Example\",\"Lease\":\"bad\\ntext\"}", "FORESITE_CONFIGURATION_INVALID"),
            ("{\"Id\":42}", "FORESITE_CONFIGURATION_INVALID"),
            ("[]", "FORESITE_CONFIGURATION_INVALID"), ("<html>SECRET</html>", "FORESITE_CONFIGURATION_INVALID") })
        {
            factory.Payload = payload;
            await Refused(request, code);
        }
        factory.Payload = new string(' ', 1048577);
        await Refused(request, "FORESITE_PAYLOAD_TOO_LARGE");
        factory.Chunked = true;
        await Refused(request, "FORESITE_PAYLOAD_TOO_LARGE");
        factory.Chunked = false;
        foreach (var id in new[] { "0", "-1", "../42", "42?all=true", "42/43", "" })
            await Refused(request with { Read = new(id) }, "FORESITE_WELL_ID_INVALID");
        await Refused(request with { ConnectionId = Guid.Empty }, "FORESITE_CONNECTION_INVALID");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        try { await pool.ReadConfigurationAsync(request, cancel.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Check(factory.Methods.All(method => method == HttpMethod.Get), "Configuration read made a source write");
        Console.WriteLine("ForeSite configuration: strict identity/shape, bounded bodies, timestamps, shared auth/revision sessions and cancellation passed (synthetic fixtures).");

        async Task Refused(ConnectionConfigurationRequest input, string code)
        {
            try { await pool.ReadConfigurationAsync(input, CancellationToken.None); }
            catch (WellSiteAutoPilotException error) when (error.Code == code && !error.Message.Contains("SECRET", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("Expected configuration refusal: " + code);
        }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Factory : HttpMessageHandler, IHttpClientFactory
    {
        public int Tokens;
        public int ConfigReads;
        public bool Expire;
        public bool Chunked;
        public string Payload = """{"Id":42,"Name":"Example","WellType":1,"FacilityId":7,"Field":"Field","Lease":"Lease","ChangeDate":"2026-11-01T01:00:00-05:00","PrivateUnused":"SECRET"}""";
        public List<HttpMethod> Methods { get; } = [];
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Methods.Add(request.Method);
            if (request.RequestUri!.AbsolutePath == "/prefix/TokenService/CreateToken")
            {
                Tokens++;
                var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                token.Headers.Add("Set-Cookie", "session=" + Tokens + "; Path=/");
                return Task.FromResult(token);
            }
            Check(request.Headers.Contains("Cookie"), "Configuration missed the ForeSite cookie");
            if (Expire) { Expire = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
            if (request.RequestUri.AbsolutePath == "/prefix/Surveillance/well/42")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"Value":{"CurrentPumpFillage":60,"LastGoodScanTime":"2026-01-15T12:00:00Z"}}""") });
            Check(request.RequestUri.AbsolutePath == "/prefix/Wells/Well/42", "Single-well route escaped its virtual directory");
            ConfigReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Chunked
                ? new StreamContent(new NonSeekable(Encoding.UTF8.GetBytes(Payload))) : new StringContent(Payload) });
        }
    }
    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
