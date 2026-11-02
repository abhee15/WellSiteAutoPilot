using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
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
        [
            new ProviderCapability(ProviderCapabilityKind.CurrentData, "Current engineering data"),
            new ProviderCapability(ProviderCapabilityKind.Command, "Send a control command"),
            new ProviderCapability(ProviderCapabilityKind.Verification, "Read a commanded value back")
        ],
        Commands:
        [
            new AdapterCommandDefinition(
                "SET_SPEED", "Set pump speed", "Sets the simulated pump speed.", "speed", "spm",
                AdapterCommandValueKind.Number, [], AdapterCommandVerification.ReadBack, "CurrentSPM", 2, 60, 30, 300),
            new AdapterCommandDefinition(
                "PUMP_RUN", "Run or stop the pump", "Starts (1) or stops (0) the simulated pump.", "state", null,
                AdapterCommandValueKind.OnOff, [], AdapterCommandVerification.ReadBack, "PumpRunning", 2, 60, 30, 300)
        ]);

    private readonly IReadOnlyList<AdapterDescriptor> _descriptors = [Simulator, ForeSiteAdapter.Descriptor, PetroVisorAdapter.Descriptor, CygNetAdapter.Descriptor];

    public IReadOnlyList<AdapterDescriptor> List() => _descriptors;

    public AdapterDescriptor? Find(string key) =>
        _descriptors.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
