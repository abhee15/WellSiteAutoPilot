using System.Text.Json;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class SourceChangeChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static void Verify()
    {
        var facts = new Dictionary<string, string> { ["wellName"] = "Well 1", ["wellType"] = "ESP", ["wellTypeId"] = "2", ["deviceType"] = "Controller A" };
        var baseline = JsonSerializer.Serialize(new ImportedSourceBaseline(1, "Well 1", facts, [], [], [], "gateway"), Options);
        DiscoveredAsset Well(Dictionary<string, string> metadata, string name = "Well 1") => new("1", name, "foresite.well", null, metadata);
        var unchanged = SourceChangeComparison.Compare(baseline, Well(facts), true, []);
        Check(unchanged.Status == "Unchanged", "same normalized source is unchanged");
        var changedFacts = new Dictionary<string, string>(facts) { ["wellType"] = "RRL", ["wellTypeId"] = "1" };
        var changed = SourceChangeComparison.Compare(baseline, Well(changedFacts), true, []);
        Check(changed.Status == "Changed" && changed.Differences.Count == 2, "well type label and stable ID changes are evidence");
        Check(SourceChangeComparison.Compare(baseline, Well(new(changedFacts.Reverse())), true, []).Fingerprint == changed.Fingerprint,
            "JSON and metadata ordering do not duplicate findings");
        Check(SourceChangeComparison.Compare(baseline, Well(new(facts) { ["deviceType"] = " Controller  A " }), true, []).Status == "Unchanged",
            "insignificant whitespace is ignored");
        var missingDevice = new Dictionary<string, string>(facts); missingDevice.Remove("deviceType");
        var missing = SourceChangeComparison.Compare(baseline, Well(missingDevice), true, []);
        Check(missing.Status == "ComparisonUnavailable" && missing.Differences.Count == 0 && missing.UnknownFields.Contains("deviceType"),
            "omitted per-well device stays unknown even when its API succeeded");
        Check(SourceChangeComparison.Compare(baseline, Well(new(facts) { ["deviceType"] = "Different" }), true, ["deviceType"]).Differences.Count == 0,
            "unavailable field cannot establish a device change");
        Check(SourceChangeComparison.Compare(baseline, Well(changedFacts), true, null).Differences.Count == 0,
            "undeclared provider field coverage remains unknown");
        var sparseFacts = new Dictionary<string, string> { ["wellName"] = "Well 1", ["wellType"] = "ESP", ["wellTypeId"] = "2" };
        var sparseBaseline = JsonSerializer.Serialize(new ImportedSourceBaseline(1, "Well 1", sparseFacts, [], [], [], "gateway"), Options);
        Check(SourceChangeComparison.Compare(sparseBaseline, Well(sparseFacts), true, []).UnknownFields.Contains("deviceType"),
            "device absence in BOTH snapshots is still unknown rather than complete coverage");
        Check(SourceChangeComparison.Compare(baseline, null, true, []).Status == "NotReturned", "complete absence is not-returned, never deletion");
        Check(SourceChangeComparison.Compare(baseline, null, false, []).Status == "ComparisonUnavailable", "partial absence proves nothing");
        Check(SourceChangeComparison.Compare("broken", Well(facts), true, []).Status == "ComparisonUnavailable", "invalid baselines are not guessed");
        var legacy = SourceChangeComparison.Compare(JsonSerializer.Serialize(facts), Well(changedFacts), true, []);
        Check(legacy.Status == "Changed" && legacy.UnknownFields.Contains("sourceName"), "legacy facts compare without inventing an original source name");
        Check(SourceChangeComparison.Compare(null, Well(facts), true, []).Status == "New", "unbound source identity is new");
        // Entire inventory, including beyond the discovery sample, must be compared by callers.
        Check(Enumerable.Range(0, 10001).Select(i => SourceChangeComparison.Compare(baseline, Well(i == 10000 ? changedFacts : facts), true, []))
            .Count(result => result.Status == "Changed") == 1, "comparison works beyond sample boundaries");
        Console.WriteLine("Source change comparison checks passed.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Source change comparison failed: " + message); }
}
