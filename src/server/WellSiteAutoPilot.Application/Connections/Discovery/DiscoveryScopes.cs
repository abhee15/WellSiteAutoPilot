using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Discovery;

/// <summary>
/// Which of the found objects a person is looking at or importing. Words must all appear in the name, id or any fact;
/// within one grouping field any of the chosen values matches, across fields all must.
/// </summary>
public sealed record DiscoveryScope(string? Query, IReadOnlyDictionary<string, IReadOnlyList<string>>? Facets)
{
    public static DiscoveryScope Everything { get; } = new(null, null);

    public bool IsEverything => string.IsNullOrWhiteSpace(Query) && (Facets is null || Facets.Count == 0);
}

public sealed record DiscoveryFacetValue(string Value, int Count);

/// <summary>The values one grouping field takes across everything found, with how many objects have each.</summary>
public sealed record DiscoveryFacet(string Key, string Label, IReadOnlyList<DiscoveryFacetValue> Values, bool HasMore);

public static class DiscoveryScopes
{
    public const int MaximumFacetValues = 200;
    public const int MaximumTerms = 10;
    public const int MaximumTermLength = 100;

    /// <summary>Applies the scope, keeping the order objects were found in.</summary>
    public static IReadOnlyList<DiscoveredAsset> Apply(
        IReadOnlyList<DiscoveredAsset> items, DiscoveryScope? scope, IReadOnlyList<DiscoveryField> fields)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fields);
        if (scope is null || scope.IsEverything) return items;

        var terms = (scope.Query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length > MaximumTerms || terms.Any(term => term.Length > MaximumTermLength))
            throw Invalid($"Search for at most {MaximumTerms} words of up to {MaximumTermLength} characters each.");

        var byKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var chosen = new List<(DiscoveryField Field, HashSet<string> Values)>();
        foreach (var (key, values) in scope.Facets ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (!byKey.TryGetValue(key, out var field))
                throw Invalid("Choose only values of the grouping facts this system offers.");
            if (values.Count > 0) chosen.Add((field, values.ToHashSet(StringComparer.Ordinal)));
        }

        return items.Where(item =>
            terms.All(term => Mentions(item, term)) &&
            chosen.All(pick => pick.Values.Contains(ImportPlanner.ValueOf(item, pick.Field)))).ToArray();
    }

    /// <summary>For each grouping field, its values and counts, most common first. A cut-off list says so.</summary>
    public static IReadOnlyList<DiscoveryFacet> Summarize(IReadOnlyList<DiscoveredAsset> items, IReadOnlyList<DiscoveryField> fields)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fields);
        var facets = new List<DiscoveryFacet>(fields.Count);
        foreach (var field in fields)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var value = ImportPlanner.ValueOf(item, field);
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }

            var ordered = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            facets.Add(new DiscoveryFacet(field.Key, field.Label,
                ordered.Take(MaximumFacetValues).Select(pair => new DiscoveryFacetValue(pair.Key, pair.Value)).ToArray(),
                ordered.Length > MaximumFacetValues));
        }

        return facets;
    }

    private static bool Mentions(DiscoveredAsset item, string term) =>
        item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        item.ExternalId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        item.Metadata.Values.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static WellSiteAutoPilotException Invalid(string message) =>
        new(FailureCodes.ValidationFailed, FailureKind.Validation, message);
}
