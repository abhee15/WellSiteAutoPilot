using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.Integration.PetroVisor;
using WellSiteAutoPilot.IntegrationGateway.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>The installed adapter descriptors and their registered read handlers.</summary>
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

    private readonly Dictionary<string, AdapterRegistration> _adapters;
    private readonly IReadOnlyList<AdapterDescriptor> _descriptors;

    public AdapterCatalog(ICygNetClient? cygnet = null, IEnumerable<AdapterRegistration>? additionalAdapters = null)
    {
        var registrations = new AdapterRegistration[]
        {
            new(Simulator, []),
            RegisteredHistoryReads.Register(ForeSiteAdapter.Descriptor, (request, services, token) =>
                services.GetRequiredService<ForeSiteConnectionMeasurements>().ReadHistoryAsync(request, token)),
            new(PetroVisorAdapter.Descriptor, []),
            RegisteredHistoryReads.Register(CygNetAdapter.Describe(cygnet), (request, services, token) =>
                services.GetRequiredService<CygNetConnectionMeasurements>().ReadHistoryAsync(request, token))
        }.Concat(additionalAdapters ?? []).ToArray();

        _adapters = registrations.ToDictionary(item => item.Descriptor.Key, StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            var operations = registration.Descriptor.ReadOperations();
            var registeredKeys = registration.Reads.ToDictionary(read => read.Operation.Key, StringComparer.Ordinal);
            if (registration.Reads.Any(read => operations.Count(operation => operation.Key == read.Operation.Key) != 1 ||
                    !operations.Contains(read.Operation) || string.IsNullOrWhiteSpace(read.Operation.ResourceKind) ||
                    read.Operation.Limits.MaximumReplyBytes is < 1 or > MeasurementPayloadLimits.MaximumResultBytes ||
                    read.Operation.Limits.TimeoutSeconds is null or < 1 or > 120) ||
                operations.Any(operation => (operation.Capability == ProviderCapabilityKind.ExtensionRead ||
                    operation.CapabilityId == "engineering.history") && !registeredKeys.ContainsKey(operation.Key)))
                throw new InvalidOperationException($"Invalid read registration for adapter '{registration.Descriptor.Key}'.");
        }

        _descriptors = registrations.Select(item => item.Descriptor).ToArray();
    }

    public IReadOnlyList<AdapterDescriptor> List() => _descriptors;

    public AdapterDescriptor? Find(string key) => _adapters.GetValueOrDefault(key)?.Descriptor;

    internal AdapterReadRegistration? FindRead(string adapterKey, string operationKey) =>
        _adapters.GetValueOrDefault(adapterKey)?.Reads.SingleOrDefault(read => read.Operation.Key == operationKey);
}
