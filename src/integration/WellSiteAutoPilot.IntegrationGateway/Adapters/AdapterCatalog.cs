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
            RegisterForeSite(),
            new(PetroVisorAdapter.Descriptor, []),
            RegisterCygNet(cygnet)
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
                    operation.Capability == ProviderCapabilityKind.Alarms ||
                    operation.CapabilityId == "engineering.history") && !registeredKeys.ContainsKey(operation.Key)))
                throw new InvalidOperationException($"Invalid read registration for adapter '{registration.Descriptor.Key}'.");
        }

        _descriptors = registrations.Select(item => item.Descriptor).ToArray();
    }

    private static AdapterRegistration RegisterCygNet(ICygNetClient? client)
    {
        var history = RegisteredHistoryReads.Register(CygNetAdapter.Describe(client), (request, services, token) =>
            services.GetRequiredService<CygNetConnectionMeasurements>().ReadHistoryAsync(request, token));
        var reads = history.Reads.ToList();
        if (client is ICygNetPointCatalogClient pointCatalog)
            reads.Add(AdapterReadRegistration.Create<CygNetPointCatalogRequest, CygNetPointCatalogResult>(
                CygNetPointCatalog.Operation, (request, invocation, _, token) =>
                    CygNetPointCatalog.ReadAsync(pointCatalog, ReadCygNetConnection(request.AssetExternalId, invocation), request, token)));
        if (client is ICygNetAlarmClient alarms)
            reads.Add(AdapterReadRegistration.Create<CygNetActiveAlarmRequest, CygNetActiveAlarmResult>(
                CygNetActiveAlarms.Operation, (request, invocation, _, token) =>
                    CygNetActiveAlarms.ReadAsync(alarms, ReadCygNetConnection(request.AssetExternalId, invocation), request, token)));
        return history with { Reads = reads };
    }

    private static CygNetConnection ReadCygNetConnection(string assetExternalId,
        AdapterReadInvocation<System.Text.Json.JsonElement> invocation)
    {
        if (assetExternalId != invocation.ExternalId ||
            CygNetAdapter.ReadConnection(invocation.Settings, out _) is not { } connection)
            throw AdapterReadEndpoints.InvalidRequest();
        return connection;
    }

    private static AdapterRegistration RegisterForeSite()
    {
        var history = RegisteredHistoryReads.Register(ForeSiteAdapter.Descriptor, (request, services, token) =>
            services.GetRequiredService<ForeSiteConnectionMeasurements>().ReadHistoryAsync(request, token));
        var latestTest = AdapterReadRegistration.Create<LatestWellTestReadRequest, LatestWellTestResult>(
            ForeSiteWellTestReference.Latest, (request, invocation, services, token) =>
            {
                if (request.AssetExternalId != invocation.ExternalId) throw AdapterReadEndpoints.InvalidRequest();
                var connection = new ConnectionMeasurementRequest(invocation.ConnectionId, invocation.ConnectionRevision,
                    invocation.Settings.ToDictionary(StringComparer.Ordinal), invocation.ExternalId, ForeSiteWellTestReference.Latest.Key);
                return services.GetRequiredService<ForeSiteConnectionMeasurements>().ReadLatestWellTestAsync(connection, request, token);
            });
        var daily = AdapterReadRegistration.Create<ForeSiteDailyAverageRequest, ForeSiteDailyAverageResult>(
            ForeSiteDailyAverageReader.Operation, (request, invocation, services, token) =>
            {
                if (request.AssetExternalId != invocation.ExternalId || !ForeSiteDailyAverageReader.IsValidRequest(request))
                    throw AdapterReadEndpoints.InvalidRequest();
                var connection = new ConnectionMeasurementRequest(invocation.ConnectionId, invocation.ConnectionRevision,
                    invocation.Settings.ToDictionary(StringComparer.Ordinal), invocation.ExternalId, ForeSiteDailyAverageReader.Operation.Key);
                return services.GetRequiredService<ForeSiteConnectionMeasurements>().ReadDailyAverageAsync(connection, request, token);
            });
        return history with { Reads = [.. history.Reads, latestTest, daily] };
    }

    public IReadOnlyList<AdapterDescriptor> List() => _descriptors;

    public AdapterDescriptor? Find(string key) => _adapters.GetValueOrDefault(key)?.Descriptor;

    internal AdapterReadRegistration? FindRead(string adapterKey, string operationKey) =>
        _adapters.GetValueOrDefault(adapterKey)?.Reads.SingleOrDefault(read => read.Operation.Key == operationKey);
}
