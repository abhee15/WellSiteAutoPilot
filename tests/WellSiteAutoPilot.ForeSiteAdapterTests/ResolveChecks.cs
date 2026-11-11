using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class ResolveChecks
{
    private static ForeSiteWellSnapshot Snapshot(string well, string surveillance, string welltest) => new(
        ForeSiteResolver.Flatten(JsonDocument.Parse(well).RootElement),
        ForeSiteResolver.Flatten(JsonDocument.Parse(surveillance).RootElement),
        ForeSiteResolver.Flatten(JsonDocument.Parse(welltest).RootElement));

    public static async Task RunAsync()
    {
        var both = Snapshot(
            """{"SurfaceLatitude":38.37,"SurfaceLongitude":-98.79}""",
            """{"Value":{"CasingPressure":234.2,"TubingPressure":107.6,"CurrentPumpFillage":99.6}}""",
            """{"AverageCasingPressure":15,"AverageTubingPressure":90}""");
        var none = Snapshot("{}", """{"Value":{"CasingPressure":null,"TubingPressure":""}}""", "{}");

        Check(ForeSiteResolver.Resolve("Latitude", both).Outcome == ResolveOutcome.Compatible, "latitude resolves from the well record");
        var casing = ForeSiteResolver.Resolve("CasingPressure", both);
        Check(casing.Outcome == ResolveOutcome.NeedsSelection && casing.Candidates.Count == 2, "casing from scan and well test needs a choice");
        var pinned = ForeSiteResolver.Resolve("CasingPressure", both, ResolveBasis.TestSample);
        Check(pinned.Outcome == ResolveOutcome.Compatible && pinned.Candidates[0].Source.EndsWith("AverageCasingPressure", StringComparison.Ordinal), "approved basis picks the well test");
        var scanOnly = ForeSiteResolver.Resolve("CasingPressure", Snapshot("{}", """{"Value":{"CasingPressure":5}}""", "{}"));
        Check(scanOnly.Outcome == ResolveOutcome.Compatible, "a single source needs no choice");
        var missing = ForeSiteResolver.Resolve("CasingPressure", none);
        Check(missing.Outcome == ResolveOutcome.NotApplicable && missing.Reasons.Count == 2, "no source reports every reason");
        Check(ForeSiteResolver.Resolve("surveillance:CurrentPumpFillage", both).Outcome == ResolveOutcome.Compatible, "a surveillance point resolves by name");
        Check(ForeSiteResolver.Resolve("CasingPresure", both).Outcome == ResolveOutcome.Misconfigured, "a typo is misconfigured");
        Check(ForeSiteResolver.ResolveCommand("CasingPressure", ["POLoadSP", "VFDMaxSPM"]).Outcome == ResolveOutcome.NotApplicable, "casing pressure is not writable");
        Check(ForeSiteResolver.ResolveCommand("setpoint:POLoadSP", ["POLoadSP"]).Outcome == ResolveOutcome.Compatible, "a writable set point resolves");

        var url = Environment.GetEnvironmentVariable("FORESITE_BASE_URL");
        if (string.IsNullOrWhiteSpace(url) || Environment.GetEnvironmentVariable("WSA_LIVE_RESOLVE") != "1")
            return;
        using var handler = new HttpClientHandler { UseDefaultCredentials = true };
        using var client = new HttpClient(handler) { BaseAddress = ForeSiteAdapter.BaseAddress(url), Timeout = TimeSpan.FromSeconds(120) };
        var provider = new ForeSiteResolveProvider(new ForeSiteSession(client));
        foreach (var well in new[] { "776", "1596" })
        {
            Console.WriteLine($"LIVE well {well}: {(await provider.GetCatalogAsync(well)).Count} surveillance points with values");
            foreach (var input in new[] { "Latitude", "Longitude", "CasingPressure", "TubingPressure", "PumpFillage" })
            {
                var result = await provider.ResolveAsync(well, input);
                var text = $"LIVE   {input} -> {result.Outcome}";
                if (result.Candidates.Count > 0)
                {
                    var value = await provider.ReadAsync(well, result.Candidates[0]);
                    text += $" [{string.Join(", ", result.Candidates.Select(c => c.Source))}] first={value.Value} {value.Unit}";
                }
                Console.WriteLine(text + (result.Reasons.Count > 0 ? " | " + string.Join(" ", result.Reasons) : ""));
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("ForeSite resolve check failed: " + message);
    }
}
