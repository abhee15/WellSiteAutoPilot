using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class HistoryChecks
{
    public static async Task RunAsync()
    {
        var client = new SimulatedCygNetClient();
        var connection = new CygNetConnection(1, "SITE", "UIS");
        var provider = new CygNetHistoricalDataProvider(client, connection);
        var start = new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
        var request = new HistoricalReadRequest("SITE.UIS::WELL_005", "PumpFillage", start, start.AddHours(2), 2);
        var result = await provider.ReadHistoryAsync(request);
        Check(result.Truncated && result.Samples.Count == 2 && result.Samples.All(sample => sample.Quality == "Uncertain") &&
            result.Source.StartsWith("cygnet-simulated", StringComparison.Ordinal) && result.Samples[0].TimestampUtc == start,
            "Simulated history lost timestamps, reliability or truncation.");
        foreach (var (bad, code) in new[] {
            (request with { AssetExternalId = "OTHER.UIS::WELL_005" }, "CYGNET_FACILITY_INVALID"),
            (request with { Quantity = "unmapped" }, "CYGNET_QUANTITY_UNSUPPORTED") })
        {
            try { await provider.ReadHistoryAsync(bad); throw new InvalidOperationException("History scope bypass."); }
            catch (WellSiteAutoPilotException error) when (error.Code == code) { }
        }
        try { await provider.GetHistoryAsync(request.AssetExternalId, request.Quantity, start, start.AddDays(31));
            throw new InvalidOperationException("Legacy history interface hid truncation."); }
        catch (WellSiteAutoPilotException error) when (error.Code == "CYGNET_HISTORY_TRUNCATED") { }
        var unconfigured = new CygNetHistoricalDataProvider(new CygNetUnconfiguredClient(), connection);
        try { await unconfigured.ReadHistoryAsync(request); throw new InvalidOperationException("Unsupported backend fell back."); }
        catch (WellSiteAutoPilotException error) when (error.Code == "CYGNET_HISTORY_UNSUPPORTED") { }
        Check(new AdapterCatalog(client).Find("cygnet")!.ReadOperations().Any(o => o.Capability == ProviderCapabilityKind.HistoricalData) &&
            !new AdapterCatalog(new CygNetUnconfiguredClient()).Find("cygnet")!.ReadOperations().Any(o => o.Capability == ProviderCapabilityKind.HistoricalData),
            "History metadata does not follow installed backend support.");
        Console.WriteLine("CygNet history: simulated values, quality, UTC, truncation, scope and unsupported-backend checks passed. Live VHS validation not performed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
