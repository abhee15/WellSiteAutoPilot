using System.Net;
using System.Text;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Providers;

internal static class SnapshotChecks
{
    private const string Complete = """
        {"Value":{"CurrentPumpFillage":0,"AveragePumpFillage":100,
          "LastGoodScanTime":"2026-10-27T00:00:00-05:00",
          "LastScanTime":"2026-10-27T08:30:00Z","CommStatus":"Failed",
          "WellTest_WaterCut":45}}
        """;

    public static async Task RunAsync()
    {
        var backend = new Backend();
        using var pool = new ForeSiteConnectionMeasurements(backend, TimeProvider.System);
        var request = new ConnectionMeasurementRequest(Guid.NewGuid(), 1,
            new Dictionary<string, string?> { ["baseUrl"] = "http://fs.example/prefix" },
            "42", "PumpFillage");

        var result = await pool.ReadSnapshotAsync(request,
            ["PumpFillage", "AveragePumpFillage"], CancellationToken.None);
        Check(result.Count == 2 && result["PumpFillage"].Value == 0 &&
            result["AveragePumpFillage"].Value == 100, "Snapshot lost zero or valid maximum.");
        var first = result["PumpFillage"];
        var second = result["AveragePumpFillage"];
        Check(first.Unit == "%" && second.Unit == "%" &&
            first.TimestampUtc == new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero) &&
            first.TimestampUtc == second.TimestampUtc &&
            first.TimestampKind == "ControllerScan" && second.TimestampKind == "ControllerScan" &&
            first.Quality == "Good" && second.Quality == "Good" &&
            first.RetrievedAtUtc is not null && first.RetrievedAtUtc == second.RetrievedAtUtc,
            "Snapshot values were not acquired with identical scan provenance and retrieval time.");
        Check(backend.Tokens == 1 && backend.StatusCalls == 1 &&
            backend.Paths.SequenceEqual(["/prefix/TokenService/CreateToken", "/prefix/Surveillance/well/42"]) &&
            backend.Methods.All(method => method == HttpMethod.Get),
            "Snapshot made duplicate status calls, escaped its prefix or used an unsafe method.");

        // The original current scalar path must retain the same behavior.
        var scalar = await pool.ReadAsync(request, CancellationToken.None);
        Check(scalar.Value == first.Value && scalar.TimestampUtc == first.TimestampUtc &&
            backend.StatusCalls == 2, "Existing scalar current read changed.");

        async Task RejectAsync(IReadOnlyCollection<string> quantities, string code, bool sourceRead)
        {
            var prior = backend.StatusCalls;
            try { await pool.ReadSnapshotAsync(request, quantities, CancellationToken.None); }
            catch (WellSiteAutoPilotException error) when (error.Code == code)
            {
                Check(sourceRead == (backend.StatusCalls == prior + 1),
                    "A rejected request performed an unexpected provider read.");
                return;
            }
            throw new InvalidOperationException("Snapshot accepted invalid requirement: " + code);
        }

        await RejectAsync([], "FORESITE_SNAPSHOT_QUANTITY_LIMIT", false);
        await RejectAsync(["PumpFillage", "AveragePumpFillage", "Extra"], "FORESITE_SNAPSHOT_QUANTITY_LIMIT", false);
        await RejectAsync(["PumpFillage", "PumpFillage"], "FORESITE_SNAPSHOT_QUANTITY_DUPLICATE", false);
        await RejectAsync(["PumpFillage", "WaterCut"], "FORESITE_QUANTITY_UNSUPPORTED", false);

        backend.Payload = """{"Value":{"CurrentPumpFillage":80,"LastGoodScanTime":"2026-10-27T05:00:00Z"}}""";
        await RejectAsync(["PumpFillage", "AveragePumpFillage"], "FORESITE_MEASUREMENT_MISSING", true);
        backend.Payload = """{"Value":{"CurrentPumpFillage":80,"AveragePumpFillage":101,"LastGoodScanTime":"2026-10-27T05:00:00Z"}}""";
        await RejectAsync(["PumpFillage", "AveragePumpFillage"], "FORESITE_MEASUREMENT_INVALID", true);
        backend.Payload = """{"Value":{"CurrentPumpFillage":80,"AveragePumpFillage":50,"LastScanTime":"2026-10-27T08:00:00Z"}}""";
        await RejectAsync(["PumpFillage", "AveragePumpFillage"], "FORESITE_TIMESTAMP_INVALID", true);
        backend.Payload = """{"Value":{"CurrentPumpFillage":80,"AveragePumpFillage":50,"LastGoodScanTime":"2026-10-27T05:00:00"}}""";
        await RejectAsync(["PumpFillage", "AveragePumpFillage"], "FORESITE_TIMESTAMP_INVALID", true);

        backend.Payload = Complete;
        var later = await pool.ReadSnapshotAsync(request with { ConnectionRevision = 2 },
            ["AveragePumpFillage", "PumpFillage"], CancellationToken.None);
        Check(later.Count == 2 && backend.Tokens == 2,
            "Snapshot reused a retired session after the connection revision changed.");

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        try
        {
            await pool.ReadSnapshotAsync(request, ["PumpFillage"], canceled.Token);
            throw new InvalidOperationException("Cancelled snapshot read was accepted.");
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("ForeSite shared status snapshot: single acquisition, provenance, zero, errors, session revision and cancellation passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Backend : IHttpClientFactory
    {
        public int Tokens { get; set; }
        public int StatusCalls { get; set; }
        public string Payload { get; set; } = Complete;
        public List<string> Paths { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public HttpClient CreateClient(string name) => new(new Handler(this));
    }

    private sealed class Handler(Backend backend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            backend.Paths.Add(request.RequestUri!.AbsolutePath);
            backend.Methods.Add(request.Method);
            if (request.RequestUri.AbsolutePath == "/prefix/TokenService/CreateToken")
            {
                backend.Tokens++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc; Path=/prefix");
                return Task.FromResult(response);
            }
            Check(request.RequestUri.AbsolutePath == "/prefix/Surveillance/well/42",
                "Snapshot requested a different well or provider route.");
            Check(request.Headers.TryGetValues("Cookie", out var cookie) &&
                cookie.Single().Contains("session=abc", StringComparison.Ordinal),
                "Snapshot did not reuse the authenticated session.");
            backend.StatusCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(backend.Payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
