using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.PetroVisor;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>The adapters this build ships, registered in code. A new adapter adds one descriptor here.</summary>
public sealed class AdapterCatalog
{
    private static readonly AdapterDescriptor Simulator = new(
        "simulator",
        "Provider Simulator",
        "A built-in simulated provider for development and demonstration.",
        1,
        [new AdapterSettingField(
            "baseUrl", "Simulator address",
            "The address the simulator listens on, for example http://127.0.0.1:5091.",
            AdapterSettingKind.Url, true)],
        [new ProviderCapability(ProviderCapabilityKind.CurrentData, "Current engineering data")]);

    private readonly IReadOnlyList<AdapterDescriptor> _descriptors = [Simulator, ForeSiteAdapter.Descriptor, PetroVisorAdapter.Descriptor];

    public IReadOnlyList<AdapterDescriptor> List() => _descriptors;

    public AdapterDescriptor? Find(string key) =>
        _descriptors.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
