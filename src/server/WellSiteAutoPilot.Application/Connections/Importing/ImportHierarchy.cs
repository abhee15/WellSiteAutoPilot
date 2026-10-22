using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Importing;

public sealed record ImportGroupingField(string Key, string? IdentityMetadataKey);

public sealed record ImportHierarchyStatus(string Status, IReadOnlyList<string>? Levels);
public sealed record LegacyImportWell(Guid AssetId, string Kind, string ExternalId, Guid? ParentId, string SnapshotJson);
public sealed record LegacyImportGroup(Guid AssetId, Guid? ParentId, string Name, bool IsActive);
public sealed record ImportedStableGroup(string Key, Guid AssetId, Guid? ParentId, string Name, string SourceLabel,
    bool IsActive, Guid TypeId, Guid? OwnerConnectionId);
public sealed record ImportedGroupKey(string Key, Guid AssetId);
public sealed record ImportHierarchyState(IReadOnlyList<string>? Levels, IReadOnlyList<LegacyImportWell> Wells,
    IReadOnlyList<LegacyImportGroup> Groups, int Revision = 0, IReadOnlyList<ImportGroupingField>? Fields = null, IReadOnlyList<ImportedStableGroup>? StableGroups = null)
{
    public ImportHierarchyStatus Status => new(Levels is not null ? "Saved" : Wells.Count > 0 ? "Legacy" : "New", Levels);
    public bool MatchesFields(IReadOnlyList<string> levels, IReadOnlyList<DiscoveryField> fields) =>
        Fields is null ? Levels is null : Fields.SequenceEqual(levels.Select(key => new ImportGroupingField(key, fields.Single(field => field.Key == key).IdentityMetadataKey)));

    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Levels, Wells, Groups, Revision, Fields, StableGroups })));

    /// <summary>Maps only proven legacy ancestors; does not move or rename any Asset.</summary>
    public IReadOnlyList<ImportedGroupKey> Adoption(Guid connectionId, IReadOnlyList<string> levels, IReadOnlyList<DiscoveryField> fields)
    {
        var groups = Groups.ToDictionary(group => group.AssetId);
        var byKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var byAsset = new Dictionary<Guid, string>();
        foreach (var well in Wells)
        {
            var baseline = ImportedSourceBaseline.Read(well.SnapshotJson);
            if (baseline is null) throw Invalid();
            var item = new DiscoveredAsset(well.ExternalId, well.ExternalId, well.Kind, null, baseline.Facts);
            if (levels.Any(key => fields.Single(field => field.Key == key).IdentityMetadataKey is { } identityKey &&
                !string.IsNullOrWhiteSpace(baseline.Facts.GetValueOrDefault(key)) &&
                string.IsNullOrWhiteSpace(baseline.Facts.GetValueOrDefault(identityKey)))) throw Invalid();
            var placement = ImportPlanner.Plan(connectionId, [item], levels, fields).Placements.Single();
            var path = new List<LegacyImportGroup>();
            var parent = well.ParentId;
            var seen = new HashSet<Guid>();
            while (parent is { } id)
            {
                if (!seen.Add(id) || !groups.TryGetValue(id, out var group) || !group.IsActive) throw Invalid();
                path.Add(group);
                if (path.Count > levels.Count) throw Invalid();
                parent = group.ParentId;
            }
            path.Reverse();
            if (path.Count != levels.Count) throw Invalid();
            for (var depth = 0; depth < path.Count; depth++)
            {
                var key = placement.GroupKeys[depth];
                if (path[depth].Name != placement.GroupPath[depth] ||
                    (byKey.TryGetValue(key, out var mapped) && mapped != path[depth].AssetId) ||
                    (byAsset.TryGetValue(path[depth].AssetId, out var mappedKey) && mappedKey != key)) throw Invalid();
                byKey[key] = path[depth].AssetId; byAsset[path[depth].AssetId] = key;
            }
        }
        return byKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ImportedGroupKey(pair.Key, pair.Value)).ToArray();
    }

    private static WellSiteAutoPilotException Invalid() => new(ConnectionFailureCodes.ImportNotAllowed, FailureKind.Conflict,
        "The chosen hierarchy cannot be safely matched to every existing imported well. Existing Assets were left unchanged. " +
        "Review the original grouping, local moves and same-named source groups before adopting it.");
}

/// <summary>Versioned accepted facts; discovery and idempotent reimport never replace this baseline.</summary>
public sealed record ImportedSourceBaseline(int FormatVersion, string? SourceName, IReadOnlyDictionary<string, string> Facts,
    IReadOnlyList<string>? Levels, IReadOnlyList<string>? GroupKeys, IReadOnlyList<string>? UnavailableFields, string? ActingAs)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Capture(PlannedPlacement placement, IReadOnlyList<string> levels, IReadOnlyList<string>? unavailable, string? actingAs) =>
        JsonSerializer.Serialize(new ImportedSourceBaseline(1, placement.SourceName, placement.Snapshot, levels, placement.GroupKeys, unavailable, actingAs), Options);

    public static ImportedSourceBaseline? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (document.RootElement.TryGetProperty("formatVersion", out var version) && version.ValueKind == JsonValueKind.Number)
            {
                var baseline = JsonSerializer.Deserialize<ImportedSourceBaseline>(json, Options);
                return baseline is { FormatVersion: 1, Facts: not null } && baseline.Facts.All(pair => pair.Value is not null) ? baseline : null;
            }
            var facts = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options);
            return facts is not null && facts.All(pair => pair.Value is not null)
                ? new(0, null, facts, null, null, null, null) : null;
        }
        catch (JsonException) { return null; }
    }
}
