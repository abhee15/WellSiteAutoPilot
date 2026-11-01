using System.Globalization;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>A fact about how an adapter may be used, for example how long one read may take.</summary>
public sealed record AdapterLimit(string Key, string Label, string Value);

/// <summary>How the saved connections that use one adapter stand right now.</summary>
public sealed record AdapterConnectionSummary(
    int Total, int Enabled, int Healthy, int NeedsAttention, int Unreachable, int NotChecked);

/// <summary>A reviewed authoring starter whose every input this adapter can serve.</summary>
public sealed record AdapterExample(string StarterId, string Label, string Description, IReadOnlyList<string> Quantities);

/// <summary>
/// Everything an author needs to know about one adapter, built from what the adapter already declares. Nothing here is
/// read from the outside system, so building it never calls the provider.
/// </summary>
public sealed record AdapterProfile(
    AdapterDescriptor Adapter,
    string? ResourceKind,
    IReadOnlyList<AdapterQuantity> Quantities,
    bool CanRead,
    bool CanWrite,
    int? MinimumCadenceSeconds,
    IReadOnlyList<AdapterLimit> Limits,
    AdapterConnectionSummary Connections,
    IReadOnlyList<AdapterExample> Examples);

public static class AdapterProfiles
{
    public static IReadOnlyList<AdapterProfile> Build(
        IReadOnlyList<AdapterDescriptor> adapters,
        IReadOnlyList<ConnectionRecord> connections,
        IReadOnlyList<AuthoringStarter> starters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(starters);
        return adapters.Select(adapter => Build(adapter, connections, starters)).ToArray();
    }

    private static AdapterProfile Build(
        AdapterDescriptor adapter, IReadOnlyList<ConnectionRecord> connections, IReadOnlyList<AuthoringStarter> starters)
    {
        var rule = AdapterReadRules.Find(adapter.Key);
        var quantities = rule is null ? [] : AdapterQuantities.For(adapter.Key);
        var canWrite = adapter.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Command);
        var limits = new List<AdapterLimit>();
        if (rule is not null && AdapterQuantities.ReadBudget(adapter.Key) is { } budget)
            limits.Add(new("readTimeout", "Time allowed for one read", string.Create(CultureInfo.InvariantCulture, $"{budget.TotalSeconds:0} s")));
        if (quantities.Any(item => item.ValueKind == AdapterValueKinds.Structured))
            limits.Add(new("structuredValueSize", "Largest structured value",
                $"{MeasurementPayloadLimits.MaximumValueBytes / 1024} KB"));
        if (adapter.CanDiscover)
        {
            limits.Add(new("discoveryObjects", "Objects listed at once", ConnectionService.MaximumDiscoveredObjects.ToString("N0", CultureInfo.InvariantCulture)));
            limits.Add(new("importObjects", "Objects imported at once", ConnectionService.MaximumImportedObjects.ToString("N0", CultureInfo.InvariantCulture)));
        }
        limits.Add(new("checkInterval", "Scheduled connection checks at most every",
            $"{ConnectionMonitorOptions.MinimumIntervalSeconds} s"));

        var mine = connections.Where(item => string.Equals(item.AdapterKey, adapter.Key, StringComparison.Ordinal)).ToArray();
        string? Outcome(ConnectionRecord item) =>
            item.IsEnabled && item.LastTest is { } test && test.TestedRevision == item.Revision ? test.Outcome : null;
        var summary = new AdapterConnectionSummary(
            mine.Length, mine.Count(item => item.IsEnabled),
            mine.Count(item => Outcome(item) == AdapterTestOutcomes.Healthy),
            mine.Count(item => Outcome(item) == AdapterTestOutcomes.NeedsAttention),
            mine.Count(item => Outcome(item) == AdapterTestOutcomes.Unreachable),
            mine.Count(item => item.IsEnabled && Outcome(item) is null));

        var served = quantities.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var examples = starters
            .Where(starter => starter.Document.Inputs.Count > 0 && starter.Document.Inputs.All(input =>
                served.TryGetValue(input.Quantity, out var quantity) && quantity.ValueKind == AdapterValueKinds.Number &&
                input.ValueSchema is null && string.Equals(input.Unit, quantity.Unit, StringComparison.Ordinal)))
            .Select(starter => new AdapterExample(starter.Id, starter.Label, starter.Description,
                starter.Document.Inputs.Select(input => input.Quantity).Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new(adapter, rule?.ResourceKind, quantities, rule is not null, canWrite, null, limits, summary, examples);
    }
}
