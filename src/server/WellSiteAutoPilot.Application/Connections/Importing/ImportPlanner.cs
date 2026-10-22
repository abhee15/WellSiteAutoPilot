using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Importing;

/// <summary>Where one discovered object would go: its own name, the groups above it (top first) and the attributes it would carry.</summary>
public sealed record PlannedPlacement(
    string ExternalId, string Kind, string Name, IReadOnlyList<string> GroupPath, IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> Snapshot, IReadOnlyList<string> GroupKeys, string SourceName);

/// <summary>One group in the planned tree. <see cref="ObjectCount"/> counts every object below it.</summary>
public sealed class PlanGroup(string name, bool isFallback, string key, string? sourceIdentity)
{
    public string Name { get; } = name;

    public string Key { get; } = key;

    public string? SourceIdentity { get; } = sourceIdentity;

    /// <summary>True when the group exists only because objects had no value for the fact it groups by.</summary>
    public bool IsFallback { get; } = isFallback;

    public int ObjectCount { get; internal set; }

    public List<PlanGroup> Children { get; } = [];

    /// <summary>A few names from the objects held directly in this group, so a person can recognise it.</summary>
    public List<string> SampleNames { get; } = [];
}

/// <summary>
/// What an import would do for one discovery and one choice of levels. <see cref="Fingerprint"/> changes whenever
/// anything about the result would change, so applying a reviewed plan can refuse if the source moved on.
/// </summary>
public sealed record ImportPlan(
    string Fingerprint,
    IReadOnlyList<string> Levels,
    IReadOnlyList<PlannedPlacement> Placements,
    IReadOnlyList<PlanGroup> Groups,
    int RootObjectCount,
    int GroupCount,
    int FallbackObjectCount);

/// <summary>
/// Turns a discovery into a tree: groups by the chosen facts in order, then the objects themselves. Pure and
/// deterministic, so the same discovery and levels always give the same plan and fingerprint.
/// </summary>
public static class ImportPlanner
{
    public const int MaximumLevels = 5;
    public const int MaximumNameLength = 256;
    private const int SampleCount = 3;

    public static ImportPlan Plan(
        Guid connectionId, IReadOnlyList<DiscoveredAsset> items, IReadOnlyList<string> levels,
        IReadOnlyList<DiscoveryField> fields, IReadOnlyDictionary<string, string>? existingGroupNames = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(fields);
        var byKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var attributeKeys = fields.Where(field => field.AsAttribute).Select(field => field.Key).ToArray();

        var placements = new List<PlannedPlacement>(items.Count);
        var fallbackObjects = 0;
        foreach (var item in items.OrderBy(item => item.ExternalId, StringComparer.Ordinal).ThenBy(item => item.ProviderType, StringComparer.Ordinal))
        {
            var path = new List<string>(levels.Count);
            var usedFallback = false;
            var keys = new List<string>();
            var parentKey = string.Empty;
            foreach (var level in levels)
            {
                var value = Clean(item.Metadata.GetValueOrDefault(level));
                if (value is null)
                {
                    usedFallback = true;
                    value = FallbackName(byKey[level]);
                }

                var field = byKey[level];
                var raw = Normalize(item.Metadata.GetValueOrDefault(level));
                var identity = field.IdentityMetadataKey is { } identityKey ? Normalize(item.Metadata.GetValueOrDefault(identityKey)) : null;
                parentKey = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] {
                    connectionId.ToString("N", CultureInfo.InvariantCulture), "1", parentKey, level, field.IdentityMetadataKey ?? string.Empty,
                    identity is not null ? "id" : raw is null ? "missing" : "value", identity ?? raw ?? string.Empty })));
                keys.Add(parentKey);
                path.Add(existingGroupNames?.GetValueOrDefault(parentKey) ?? value);
            }

            if (usedFallback) fallbackObjects++;
            var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in attributeKeys)
            {
                if (Clean(item.Metadata.GetValueOrDefault(key)) is { } attribute) attributes[key] = attribute;
            }

            var name = Clean(item.DisplayName) ?? "Object " + item.ExternalId;
            placements.Add(new PlannedPlacement(item.ExternalId, item.ProviderType, name, path, attributes, item.Metadata, keys, item.DisplayName));
        }

        var (roots, groupCount, rootObjects) = BuildTree(placements, levels, byKey);
        return new ImportPlan(Fingerprint(connectionId, levels, placements), levels.ToArray(), placements, roots,
            rootObjects, groupCount, fallbackObjects);
    }

    private static (List<PlanGroup> Roots, int GroupCount, int RootObjects) BuildTree(
        List<PlannedPlacement> placements, IReadOnlyList<string> levels, Dictionary<string, DiscoveryField> byKey)
    {
        var roots = new List<PlanGroup>();
        var index = new Dictionary<string, PlanGroup>(StringComparer.Ordinal);
        var rootObjects = 0;
        foreach (var placement in placements)
        {
            if (placement.GroupPath.Count == 0)
            {
                rootObjects++;
                continue;
            }

            var key = string.Empty;
            PlanGroup? parent = null;
            for (var depth = 0; depth < placement.GroupPath.Count; depth++)
            {
                var name = placement.GroupPath[depth];
                key = placement.GroupKeys[depth];
                if (!index.TryGetValue(key, out var group))
                {
                    group = new PlanGroup(name, Normalize(placement.Snapshot.GetValueOrDefault(levels[depth])) is null, key,
                        byKey[levels[depth]].IdentityMetadataKey is { } idKey ? placement.Snapshot.GetValueOrDefault(idKey) : null);
                    index[key] = group;
                    (parent?.Children ?? roots).Add(group);
                }

                group.ObjectCount++;
                parent = group;
            }

            if (parent!.SampleNames.Count < SampleCount) parent.SampleNames.Add(placement.Name);
        }

        Sort(roots);
        return (roots, index.Count, rootObjects);
    }

    private static void Sort(List<PlanGroup> groups)
    {
        groups.Sort((a, b) =>
        {
            var order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
        });
        foreach (var group in groups) Sort(group.Children);
    }

    public static string FallbackName(DiscoveryField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Clean(field.EmptyGroupName) ?? "Unspecified: " + field.Label;
    }

    /// <summary>The group name an object would get for a field: its own value, or the field's fallback when it has none.</summary>
    public static string ValueOf(DiscoveredAsset item, DiscoveryField field)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(field);
        return Clean(item.Metadata.GetValueOrDefault(field.Key)) ?? FallbackName(field);
    }

    private static string? Normalize(string? value)
    {
        var visible = new string((value ?? string.Empty).Trim().Where(character => !char.IsControl(character)).ToArray());
        return string.IsNullOrWhiteSpace(visible) ? null : visible;
    }

    private static string? Clean(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : normalized.Length <= MaximumNameLength ? normalized : normalized[..MaximumNameLength];
    }

    private static string Fingerprint(Guid connectionId, IReadOnlyList<string> levels, List<PlannedPlacement> placements)
    {
        // Structured serialization keeps delimiters in provider values from colliding with plan structure.
        var payload = new { connectionId, levels, placements = placements.Select(placement => new {
            placement.ExternalId, placement.Kind, placement.Name, placement.GroupPath, placement.GroupKeys,
            attributes = placement.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            facts = placement.Snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal), placement.SourceName }) };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }
}
