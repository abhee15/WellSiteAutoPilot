using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>A provider an author can write a module for. The list comes from what the Server can actually read, not from connections.</summary>
public sealed record GuidedProviderOption(string AdapterKey, string DisplayName, bool CurrentValuesOnly,
    IReadOnlyList<GuidedExternalSource> ExternalSources);

/// <summary>A source other than the primary provider that one input can be marked as read from.</summary>
public sealed record GuidedExternalSource(string Key, string DisplayName);

/// <summary>Every guided draft names one primary provider at creation; it is stored in canonical form and never changes.</summary>
public static class GuidedProviders
{
    public static IReadOnlyList<GuidedProviderOption> List() =>
    [
        // WAMI's model file comes from the ForeSite well, so only ForeSite modules can name it.
        new(AdapterReadRules.ForeSiteKey, "ForeSite", CurrentValuesOnly: false, [new("wami", "WAMI")]),
        new(AdapterReadRules.CygNetKey, "CygNet", CurrentValuesOnly: true, []),
        // The only provider that publishes commands, so the one a rule can send a setpoint change through.
        new(AdapterReadRules.SimulatorKey, "Provider simulator", CurrentValuesOnly: true, [])
    ];

    /// <summary>The external sources a module written for this provider may use; none for an unknown provider.</summary>
    public static IReadOnlyList<GuidedExternalSource> ExternalSourcesFor(string? adapterKey) =>
        List().FirstOrDefault(item => string.Equals(item.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase))?.ExternalSources ?? [];

    /// <summary>Requires a known provider and rewrites the manifest key to the rule's canonical spelling.</summary>
    public static GuidedModuleDraftContent Canonicalize(GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var key = content.Manifest.RequiredAdapterKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_PROVIDER_REQUIRED", FailureKind.Validation,
                "Choose the provider this module is written for (ForeSite, CygNet or the simulator).");
        var option = List().FirstOrDefault(item => string.Equals(item.AdapterKey, key, StringComparison.OrdinalIgnoreCase)) ??
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_PROVIDER_UNKNOWN", FailureKind.Validation,
                "That provider is not supported for module authoring.");
        return content with { Manifest = content.Manifest with { RequiredAdapterKey = option.AdapterKey } };
    }
}
