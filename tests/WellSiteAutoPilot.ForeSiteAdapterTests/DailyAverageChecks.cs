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

        const string phaseUnits = """
            {"TotalFluidRateInferred":{"UnitKey":"STB/d"},"OilRateInferred":{"UnitKey":"STB/d"},
             "GasRateInferred":{"UnitKey":"Mscf/d","unitkey":"wrong"},"WaterRateInferred":{"UnitKey":"STB/d"}}
            """;
        const string phaseFields = """
            420,"OilRateInferred":0,"GasRateInferred":1,"WaterRateInferred":null,"Duration":12}
            """;
        var phases = Row.Replace("420}", phaseFields, StringComparison.Ordinal);
        result = await ReadAsync(Envelope(phases, phaseUnits));
        if (result.ObservedQuantities is not { Count: 1 } observed ||
            observed[0] is not { Quantity: "OilRateInferred", Unit: "STB/d" } || result.Days[0].Value != 420)
            throw new InvalidOperationException("Discovery must retain zero with source units and exclude malformed, missing or metadata fields.");

        // Any populated numeric field the provider gives a unit for is offered, not only a fixed list.
        const string wellUnits = """
            {"TotalFluidRateInferred":{"UnitKey":"STB/d"},"THP":{"UnitKey":"psi"},"CHP":{"UnitKey":"psi"},
             "Bad-Name":{"UnitKey":"x"},"NoValue":{"UnitKey":"psi"}}
            """;
        const string wellFields = """
            420,"THP":85.5,"CHP":0,"Bad-Name":1,"NoValue":null,"Unitless":3}
            """;
        result = await ReadAsync(Envelope(Row.Replace("420}", wellFields, StringComparison.Ordinal), wellUnits));
        if (result.ObservedQuantities is not { Count: 2 } wellObserved ||
            wellObserved[0] is not { Quantity: "CHP", Unit: "psi" } || wellObserved[1] is not { Quantity: "THP", Unit: "psi" })
            throw new InvalidOperationException("Discovery must offer every populated field the provider gives a unit for, in a stable order, and nothing else.");

        // The latest finished production day supplies any populated provider field ("yesterday"), with its own unit.
        const string day2 = """
            {"Id":8,"WellId":101,"StartDateTime":"2026-11-02T06:00:00Z","EndDateTime":"2026-11-03T05:59:59Z","TotalFluidRateInferred":430,"CyclesYesterday":0,"RunTime":23.5}
            """;
        const string dayUnits = """
            {"TotalFluidRateInferred":{"UnitKey":"STB/d"},"CyclesYesterday":{"UnitKey":"count"},"RunTime":{"UnitKey":"s"}}
            """;
        result = await ReadAsync(Envelope(Row.Replace("420}", "420,\"CyclesYesterday\":9}", StringComparison.Ordinal) + "," + day2, dayUnits), Request2);
        var after = DateTimeOffset.Parse("2026-11-03T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var cycles = ForeSiteLatestDailyValue.Select(result, "CyclesYesterday", after);
        var runTime = ForeSiteLatestDailyValue.Select(result, "RunTime", after);
        if (cycles is not { Value: 0, Unit: "count", Quality: "Uncertain" } || cycles.TimestampUtc != DateTimeOffset.Parse("2026-11-03T05:59:59Z", System.Globalization.CultureInfo.InvariantCulture) ||
            cycles.TimestampKind != ForeSiteLatestDailyValue.TimestampKind || runTime is not { Value: 23.5m, Unit: "s" })
            throw new InvalidOperationException("The latest finished day must supply the field, its unit and the production-day end time; zero is a value.");
        // A day still in progress is never used, and a field the finished days do not report is missing, not stale.
        var during = DateTimeOffset.Parse("2026-11-03T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        if (ForeSiteLatestDailyValue.Select(result, "CyclesYesterday", during).Value != 9)
            throw new InvalidOperationException("An unfinished production day must not be read; the previous finished day is used.");
        try { ForeSiteLatestDailyValue.Select(result, "RunTime", during); throw new InvalidOperationException("A field missing from every finished day must fail."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_MEASUREMENT_MISSING") { }
        try { ForeSiteLatestDailyValue.Select(result with { UnitsByQuantity = new Dictionary<string, string>() }, "RunTime", after); throw new InvalidOperationException("A field with no unit must fail."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_DAILY_UNIT_MISSING") { }

        // A selected provider field is read per production day with its own unit; a day without it is empty, not zero.
        var selectedRequest = Request2 with { Quantity = "RunTime" };
        result = await ReadAsync(Envelope(Row + "," + day2, dayUnits), selectedRequest);
        if (result is not { Quantity: "RunTime", Unit: "s" } || result.Days.Count != 2 || result.Days[0].Value is not null || result.Days[1].Value != 23.5m)
            throw new InvalidOperationException("A selected daily quantity must be projected per day with its own unit.");
        await Reject(Envelope(Row + "," + day2, "{}"), "FORESITE_DAILY_UNIT_MISSING");
        // A catalogued field in another unit system is refused, and a unitless count needs no unit entry.
        try { await ReadAsync(Envelope(Row + "," + day2, dayUnits.Replace("\"s\"", "\"h\"", StringComparison.Ordinal)), selectedRequest); throw new InvalidOperationException("A unit differing from the catalogue must be refused."); }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_UNIT_MISMATCH") { }
        const string countDay = """
            {"Id":9,"WellId":101,"StartDateTime":"2026-11-02T06:00:00Z","EndDateTime":"2026-11-03T05:59:59Z","NumberOfCycles":12}
            """;
        result = await ReadAsync(Envelope(countDay, "{}"), Request2 with { Quantity = "NumberOfCycles" });
        if (result is not { Unit: "count" } || result.Days.Single().Value != 12)
            throw new InvalidOperationException("A catalogued count carries the count unit although the provider states none.");
        try { await ReadAsync(Envelope(Row), Request2 with { Quantity = "Bad-Name" }); throw new InvalidOperationException("An invalid quantity name must be rejected."); }
        catch (ForeSiteMeasurementException) { }

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

    private static readonly ForeSiteDailyAverageRequest Request2 = new("101", "2026-11-01T00:00:00Z", "2026-11-04T00:00:00Z");

    private static async Task<ForeSiteDailyAverageResult> ReadAsync(string json, ForeSiteDailyAverageRequest? request = null)
    {
        using var client = new HttpClient(new Source(json)) { BaseAddress = new Uri("http://foresite.test/prefix/") };
        return await new ForeSiteDailyAverageReader(new(client)).ReadAsync(request ?? Request, CancellationToken.None);
    }

    private sealed class Source(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Daily averages must use GET.");
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/prefix/TokenService/CreateToken" => new HttpResponseMessage(HttpStatusCode.OK),
                "/prefix/Surveillance/Well/DailyAverage/101/2026-11-01T00:00:00Z/2026-11-03T00:00:00Z" or
                "/prefix/Surveillance/Well/DailyAverage/101/2026-11-01T00:00:00Z/2026-11-04T00:00:00Z" =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
                _ => throw new InvalidOperationException("Unexpected ForeSite route.")
            });
        }
    }
}
