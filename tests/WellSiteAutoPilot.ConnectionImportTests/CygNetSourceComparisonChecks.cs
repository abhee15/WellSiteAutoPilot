using System.Text.Json;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class CygNetSourceComparisonChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static void Run()
    {
        var facts = new Dictionary<string, string> { ["facilityType"] = "RRL", ["description"] = "" };
        var baseline = JsonSerializer.Serialize(new ImportedSourceBaseline(1, "WELL1", facts, [], [], [], "service"), Options);
        var observed = new DiscoveredAsset("SITE.UIS::WELL1", "WELL1", "cygnet.facility", null, facts);
        if (SourceChangeComparison.Compare(baseline, observed, true, []).Status != "Unchanged") throw new InvalidOperationException("Known blank CygNet facts became unknown.");
        var changed = observed with { Metadata = new Dictionary<string, string> { ["facilityType"] = "ESP", ["description"] = "New description" } };
        if (SourceChangeComparison.Compare(baseline, changed, true, []).Differences.Count != 2) throw new InvalidOperationException("CygNet fact change was lost.");
        if (SourceChangeComparison.Compare(baseline, null, false, []).Status != "ComparisonUnavailable") throw new InvalidOperationException("Partial CygNet discovery invented removal.");
        Console.WriteLine("CygNet source comparison checks passed.");
    }
}
