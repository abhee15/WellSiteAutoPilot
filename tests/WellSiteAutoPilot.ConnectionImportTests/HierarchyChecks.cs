using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class HierarchyChecks
{
    public static void Verify()
    {
        var connection = Guid.NewGuid();
        DiscoveryField[] fields = [new("asset", "Asset", "Unassigned", false, "assetId"), new("device", "Device", "Unknown")];
        string[] levels = ["asset", "device"];
        DiscoveredAsset Well(string id, string assetId, string asset = "Same name", string? device = "RPC") =>
            new(id, "Original well", "well", null, new Dictionary<string, string> { ["assetId"] = assetId, ["asset"] = asset,
                ["device"] = device ?? "" });
        var a = Well("1", "101"); var b = Well("2", "202");
        var plan = ImportPlanner.Plan(connection, [a, b], levels, fields);
        Check(plan.Groups.Count == 2 && plan.GroupCount == 4, "same names with distinct source IDs stay separate");
        Check(plan.Placements[0].GroupKeys[0] != plan.Placements[1].GroupKeys[0], "source identity determines the group");
        var renamed = ImportPlanner.Plan(connection, [Well("1", "101", "Renamed source")], levels, fields);
        Check(renamed.Placements[0].GroupKeys.SequenceEqual(plan.Placements[0].GroupKeys), "source label rename preserves stable keys");
        Check(ImportPlanner.Plan(Guid.NewGuid(), [a], levels, fields).Placements[0].GroupKeys[0] != plan.Placements[0].GroupKeys[0],
            "Connection ownership is part of identity");
        var missing = new DiscoveredAsset("3", "Missing", "well", null, new Dictionary<string, string>());
        var actualFallbackName = new DiscoveredAsset("4", "Named", "well", null, new Dictionary<string, string> { ["asset"] = "Unassigned" });
        var fallbacks = ImportPlanner.Plan(connection, [missing, actualFallbackName], ["asset"], fields);
        Check(fallbacks.Groups.Count == 2 && fallbacks.Groups.Count(group => group.IsFallback) == 1, "fallback labels never collapse real values");
        var longA = Well("5", "", new string('x', 256) + "A"); var longB = Well("6", "", new string('x', 256) + "B");
        Check(ImportPlanner.Plan(connection, [longA, longB], ["asset"], fields).Groups.Count == 2, "display truncation never defines identity");
        var otherKind = a with { ProviderType = "tank" };
        Check(ImportPlanner.Plan(connection, [a, otherKind], [], fields).Fingerprint ==
            ImportPlanner.Plan(connection, [otherKind, a], [], fields).Fingerprint, "composite identities have deterministic ordering");
        Check(ImportPlanner.Plan(connection, [a], [], fields).Fingerprint != ImportPlanner.Plan(connection, [Well("1", "999")], [], fields).Fingerprint,
            "a source-only identity fact participates in reviewed fingerprints");
        var localLabels = new Dictionary<string, string> { [plan.Placements[0].GroupKeys[0]] = "Local group name" };
        Check(ImportPlanner.Plan(connection, [a], levels, fields, localLabels).Placements[0].GroupPath[0] == "Local group name",
            "import review retains the actual reused group label");
        Check(IConnectionImportRepository.BindingKey("a\u0001b", "c") != IConnectionImportRepository.BindingKey("a", "b\u0001c"),
            "composite binding keys do not collide on provider delimiters");
        var baselineJson = ImportedSourceBaseline.Capture(plan.Placements[0], levels, ["device"], "CORP\\gateway");
        var baseline = ImportedSourceBaseline.Read(baselineJson)!;
        Check(baseline.FormatVersion == 1 && baseline.SourceName == "Original well" && baseline.Facts["assetId"] == "101" &&
            baseline.GroupKeys!.SequenceEqual(plan.Placements[0].GroupKeys) && baseline.UnavailableFields!.SequenceEqual(["device"]), "versioned baseline retains source provenance and coverage");
        var legacy = ImportedSourceBaseline.Read("{\"asset\":\"Same name\",\"assetId\":\"101\",\"device\":\"RPC\"}")!;
        Check(legacy.FormatVersion == 0 && legacy.SourceName is null && legacy.Levels is null, "legacy facts do not fabricate source names or recipes");
        Check(ImportedSourceBaseline.Read("{\"asset\":null}") is null && ImportedSourceBaseline.Read("[]") is null &&
            ImportedSourceBaseline.Read("{\"formatVersion\":999,\"facts\":{}}") is null, "invalid and unsupported baselines require review");
        var rootId = Guid.NewGuid(); var deviceId = Guid.NewGuid();
        var well = new LegacyImportWell(Guid.NewGuid(), "well", "1", deviceId, "{\"asset\":\"Same name\",\"assetId\":\"101\",\"device\":\"RPC\"}");
        LegacyImportGroup[] groups = [new(rootId, null, "Same name", true), new(deviceId, rootId, "RPC", true)];
        var state = new ImportHierarchyState(null, [well], groups);
        Check(state.Fingerprint == (state with { }).Fingerprint, "hierarchy fingerprints are deterministic and exclude computed getters");
        var adoption = state.Adoption(connection, levels, fields);
        Check(adoption.Count == 2 && adoption.Any(group => group.AssetId == rootId && group.Key == plan.Placements[0].GroupKeys[0]),
            "legacy adoption maps verified existing IDs without moving wells");
        Reject(state with { Wells = [well with { ParentId = rootId }] }, levels, "locally moved well cannot be silently adopted");
        Reject(state with { Groups = [groups[0] with { Name = "Local rename" }, groups[1]] }, levels, "ambiguous renamed group needs review");
        Reject(state with { Groups = [groups[0] with { ParentId = deviceId }, groups[1]] }, levels, "cycles are refused");
        Reject(state with { Wells = [well, well with { AssetId = Guid.NewGuid(), ExternalId = "2", SnapshotJson = well.SnapshotJson.Replace("101", "202", StringComparison.Ordinal) }] },
            levels, "a legacy same-name group holding different source IDs cannot be split by guessing");
        Reject(state with { Wells = [well with { SnapshotJson = "{}" }] }, levels, "missing facts cannot establish a grouped recipe");
        Check(new ImportHierarchyState(null, [well with { ParentId = null }], []).Adoption(connection, [], fields).Count == 0,
            "flat legacy wells can establish a flat canonical recipe");
        Check(new ImportHierarchyState([], [], []).Status.Status == "Saved" && new ImportHierarchyState(null, [], []).Status.Status == "New" &&
            state.Status.Status == "Legacy", "an established flat hierarchy differs from new or legacy state");

        void Reject(ImportHierarchyState candidate, string[] recipe, string message)
        {
            try { candidate.Adoption(connection, recipe, fields); throw new InvalidOperationException(message); }
            catch (WellSiteAutoPilotException error) when (error.Code == "CONNECTION_IMPORT_NOT_ALLOWED") { }
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Hierarchy check failed: " + message);
    }
}
