using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class DailyAverageChecks
{
    private const string Row = """
        {"Id":7,"WellId":101,"StartDateTime":"2026-11-01T05:00:00Z","EndDateTime":"2026-11-02T05:59:59.9900000Z","TotalFluidRateInferred":420}
        """;
    private const string Unit = """{"TotalFluidRateInferred":{"UnitKey":"STB/d"}}""";
    private static readonly ForeSiteDailyAverageRequest Request = new("101", "2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z");

    public static async Task RunAsync()
    {
        var result = await ReadAsync(Envelope(Row));
        if (result is not { AssetExternalId: "101", Unit: "STB/d" } ||
            result.Days.Single() is not { Id: "7", Value: 420 } day ||
            day.End - day.Start != TimeSpan.FromMilliseconds(89_999_990))
            throw new InvalidOperationException("ForeSite daily record shape or DST boundary changed.");

        await Reject(Envelope(Row + "," + Row), "FORESITE_DAILY_RESPONSE_INVALID");
        await Reject(Envelope(Row + "," + Row.Replace("\"Id\":7", "\"Id\":8", StringComparison.Ordinal)), "FORESITE_DAILY_RESPONSE_INVALID");
        await Reject(Envelope(Row.Replace("05:00:00Z", "05:00:00", StringComparison.Ordinal)), "FORESITE_DAILY_RESPONSE_INVALID");
        await Reject(Envelope(Row.Replace("\"WellId\":101", "\"WellId\":102", StringComparison.Ordinal)), "FORESITE_DAILY_WELL_MISMATCH");
        await Reject(Envelope(Row, "{}"), "FORESITE_DAILY_UNIT_MISSING");
        await Reject(Envelope(string.Join(',', Enumerable.Repeat(Row, 65))), "FORESITE_DAILY_ROW_LIMIT");
        await Reject(new string(' ', HistoricalReadLimits.MaximumSourceBytes + 1), "FORESITE_PAYLOAD_TOO_LARGE");
        Console.WriteLine("ForeSite daily averages: recorded shape, period boundaries, identity, malformed rows, units and source bounds passed.");
    }

    private static string Envelope(string rows, string unit = Unit) => "{\"Units\":" + unit + ",\"Values\":[" + rows + "]}";

    private static async Task Reject(string json, string code)
    {
        try { await ReadAsync(json); }
        catch (ForeSiteMeasurementException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private static async Task<ForeSiteDailyAverageResult> ReadAsync(string json)
    {
        using var client = new HttpClient(new Source(json)) { BaseAddress = new Uri("http://foresite.test/prefix/") };
        return await new ForeSiteDailyAverageReader(new(client)).ReadAsync(Request, CancellationToken.None);
    }

    private sealed class Source(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Daily averages must use GET.");
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/prefix/TokenService/CreateToken" => new HttpResponseMessage(HttpStatusCode.OK),
                "/prefix/Surveillance/Well/DailyAverage/101/2026-11-01T00:00:00Z/2026-11-03T00:00:00Z" =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
                _ => throw new InvalidOperationException("Unexpected ForeSite route.")
            });
        }
    }
}
