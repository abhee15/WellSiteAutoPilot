using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
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

    private readonly IReadOnlyList<AdapterDescriptor> _descriptors = [Simulator, ForeSiteAdapter.Descriptor, PetroVisorAdapter.Descriptor, CygNetAdapter.Descriptor];

    public static IReadOnlyList<AdapterSettingProblem> Validate(AdapterDescriptor descriptor, IReadOnlyDictionary<string, string?> settings)
    {
        var result = AdapterSettingsValidator.Validate(descriptor, settings).ToList();
        if (descriptor.Key == CygNetAdapter.Key)
        {
            if (CygNetAdapter.ReadConnection(settings, out var problem) is null) result.Add(new("site", problem!));
            if (CygNetAdapter.ReadMappings(settings) is null) result.Add(new(CygNetAdapter.MappingsSetting,
                "Enter a JSON object of quantities, each with a valid udc and unit. Names must be unique; at most 128 mappings."));
        }
        return result;
    }

    public IReadOnlyList<AdapterDescriptor> List() => _descriptors;

    public AdapterDescriptor? Find(string key) =>
        _descriptors.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
