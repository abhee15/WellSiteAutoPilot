using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class HistoryChecks
{
    public static async Task RunAsync()
    {
        var client = new StubCygNetClient();
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
        var malformed = new CygNetHistoricalDataProvider(new Fixture(result with { Samples = result.Samples.Reverse().ToArray() }), connection);
        try { await malformed.ReadHistoryAsync(request); throw new InvalidOperationException("Unsorted provider samples accepted."); }
        catch (WellSiteAutoPilotException error) when (error.Code == "CYGNET_HISTORY_INVALID") { }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        try { await provider.ReadHistoryAsync(request, cancelled.Token); throw new InvalidOperationException("Cancelled history read proceeded."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("CygNet history: simulated values, quality, UTC, truncation, scope and unsupported-backend checks passed. Live VHS validation not performed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(HistoricalReadResult result) : ICygNetClient, ICygNetHistoryClient
    {
        public Task<HistoricalReadResult> ReadHistoryAsync(CygNetConnection connection, string facilityId, HistoricalReadRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
        public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EngineeringValue> ReadCurrentAsync(CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

}
