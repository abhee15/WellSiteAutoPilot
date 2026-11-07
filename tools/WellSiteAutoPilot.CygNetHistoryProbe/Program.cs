using System.Security.Principal;
using System.Text.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

if (args.Length is not (2 or 4) || args[0] != "--request" || args.Length == 4 && args[2] != "--samples-file")
{
    Console.Error.WriteLine("Usage: --request <ConnectionHistoryRequest.json> [--samples-file <private-output.json>]");
    return 2;
}
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
try
{
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var request = JsonSerializer.Deserialize<ConnectionHistoryRequest>(await File.ReadAllTextAsync(args[1], budget.Token), json)
        ?? throw new ArgumentException("Missing history request.");
    HistoricalReadLimits.Validate(request.Read);
    if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
        AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
        CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection)
        throw new ArgumentException("Invalid saved-connection test request.");
    var defaults = CygNetProviderOptions.DefaultQuantities;
    var native = new CygNetApiClient(new((ushort)connection.Domain, defaults));
    // The probe-only wrapper exercises the shared history scope/reply validator without enabling the live Gateway capability.
    var result = await new CygNetHistoricalDataProvider(new ProbeClient(native), connection).ReadHistoryAsync(request.Read, budget.Token);
    if (args.Length == 4) await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(result, json), budget.Token);
    using var identity = WindowsIdentity.GetCurrent();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        validationLevel = "LicensedHostReadOnlyProbe", liveReadSucceeded = true,
        actingAs = identity.Name,
        liveHistoryAdvertised = CygNetAdapter.Describe(native).ReadOperations().Any(operation => operation.Capability == ProviderCapabilityKind.HistoricalData),
        sampleCount = result.Samples.Count, result.Truncated, result.Aggregation, result.Diagnostics,
        firstUtc = result.Samples.FirstOrDefault()?.TimestampUtc, lastUtc = result.Samples.LastOrDefault()?.TimestampUtc,
        qualityCounts = result.Samples.GroupBy(value => value.Quality).ToDictionary(group => group.Key, group => group.Count()),
        units = result.Samples.Select(value => value.Unit).Distinct().ToArray(),
        historianAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName())
            .Where(name => name.Name?.StartsWith("CygNet.", StringComparison.Ordinal) == true && name.Name.Contains("Historian", StringComparison.Ordinal))
            .Select(name => new { name.Name, version = name.Version?.ToString() }).ToArray()
    }, json));
    return 0;
}
catch (Exception error)
{
    var code = error switch
    {
        CygNetReadException read => read.Code,
        WellSiteAutoPilotException failure => failure.Code,
        OperationCanceledException => "CYGNET_HISTORY_PROBE_TIMEOUT",
        ArgumentException or JsonException => "CYGNET_HISTORY_PROBE_REQUEST_INVALID",
        _ => "CYGNET_HISTORY_PROBE_FAILED"
    };
    Console.Error.WriteLine(JsonSerializer.Serialize(new { validationLevel = "LicensedHostReadOnlyProbe", liveReadSucceeded = false, code }, json));
    return 1;
}

internal sealed class ProbeClient(CygNetApiClient native) : ICygNetClient, ICygNetHistoryClient
{
    public Task<HistoricalReadResult> ReadHistoryAsync(CygNetConnection connection, string facilityId, HistoricalReadRequest request, CancellationToken cancellationToken) =>
        native.ReadHistoryAsync(connection, facilityId, request, cancellationToken);
    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => native.CheckAsync(connection, cancellationToken);
    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) => native.ListFacilitiesAsync(connection, cancellationToken);
    public Task<EngineeringValue> ReadCurrentAsync(CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) => native.ReadCurrentAsync(connection, facilityId, quantity, cancellationToken);
}
