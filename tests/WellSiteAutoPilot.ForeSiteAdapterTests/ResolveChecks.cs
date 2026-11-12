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
        var unitDoc = JsonDocument.Parse("""{"Units":{"CasingPressure":{"UnitKey":"psia"},"Other":{"Precision":1}},"Value":{"CasingPressure":234.2}}""").RootElement;
        var withUnits = both with { SurveillanceUnits = ForeSiteResolver.Units(unitDoc) };
        Check(ForeSiteResolver.Units(unitDoc).Count == 1, "only fields that report a unit key are listed");
        Check(ForeSiteResolver.Resolve("surveillance:CasingPressure", withUnits).Candidates[0].ReadPlan == "surveillance|CasingPressure|psia",
            "a surveillance point carries the unit ForeSite reports, not an assumed psi");
        Check(ForeSiteResolver.Resolve("CasingPresure", both).Outcome == ResolveOutcome.Misconfigured, "a typo is misconfigured");
        Check(ForeSiteResolver.ResolveCommand("CasingPressure", ["POLoadSP", "VFDMaxSPM"]).Outcome == ResolveOutcome.NotApplicable, "casing pressure is not writable");
        Check(ForeSiteResolver.ResolveCommand("setpoint:POLoadSP", ["POLoadSP"]).Outcome == ResolveOutcome.Compatible, "a writable set point resolves");

        const string setPoints = """[{"SetPointsList":[{"ElementId":"IdealPF","Description":"Ideal Pump Fillage","UnitKey":"%","OriginalValue":"80","RangeLow":0,"RangeHigh":100,"IsReadOnly":false},{"ElementId":"Locked","OriginalValue":"1","IsReadOnly":true}]}]""";
        var plan = ForeSiteSetPoints.Plan("776", setPoints, "IdealPF", 1);
        Check(plan.Allowed && plan.Current == 80 && plan.Proposed == 81, "set point plan adds the delta");
        Check(!ForeSiteSetPoints.Plan("776", setPoints, "IdealPF", 21).Allowed, "a value beyond the range is refused");
        Check(!ForeSiteSetPoints.Plan("776", setPoints, "Locked", 1).Allowed, "a read-only set point is refused");
        Check(!ForeSiteSetPoints.Plan("776", setPoints, "CasingPressure", 1).Allowed, "casing pressure is not a set point");

        var url = Environment.GetEnvironmentVariable("FORESITE_BASE_URL");
        if (string.IsNullOrWhiteSpace(url) || Environment.GetEnvironmentVariable("WSA_LIVE_RESOLVE") != "1")
            return;
        using var handler = new HttpClientHandler { UseDefaultCredentials = true };
        using var client = new HttpClient(handler) { BaseAddress = ForeSiteAdapter.BaseAddress(url), Timeout = TimeSpan.FromSeconds(120) };
        var provider = new ForeSiteResolveProvider(new ForeSiteSession(client));
        foreach (var well in new[] { "776", "1596" })
        {
            try
            {
                var live = await provider.PlanSetPointChangeAsync(well, "IdealPF", 1);
                Console.WriteLine($"LIVE   DRY-RUN {live.SetPoint}: {live.Current} -> {live.Proposed} {live.Unit} allowed={live.Allowed} {live.Refusal}");
            }
            catch (Exception ex) { Console.WriteLine($"LIVE   DRY-RUN failed: {ex.Message}"); }
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
