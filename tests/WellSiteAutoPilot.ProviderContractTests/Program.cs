using WellSiteAutoPilot.Integration.Contracts.Providers;

var cygnet = new ProviderDescriptor(
    "cygnet-primary",
    "CygNet",
    "SCADA",
    "1.0.0",
    "existing-provider-version",
    [
        new ProviderCapability(ProviderCapabilityKind.CurrentData, "Current engineering data"),
        new ProviderCapability(ProviderCapabilityKind.HistoricalData, "Historical engineering data"),
        new ProviderCapability(ProviderCapabilityKind.Alarms, "Alarms"),
        new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Asset discovery"),
        new ProviderCapability(ProviderCapabilityKind.Command, "Governed commands"),
        new ProviderCapability(ProviderCapabilityKind.Verification, "Command verification")
    ]);

var wami = new ProviderDescriptor(
    "wami-primary",
    "WAMI",
    "EngineeringCalculation",
    "1.0.0",
    "existing-provider-version",
    [
        new ProviderCapability(ProviderCapabilityKind.Calculation, "Engineering calculations")
    ]);

if (!cygnet.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Command) ||
    cygnet.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Calculation))
{
    throw new InvalidOperationException("CygNet semantic capability example is invalid.");
}

if (wami.Capabilities.Count != 1 ||
    wami.Capabilities.Single().Kind != ProviderCapabilityKind.Calculation)
{
    throw new InvalidOperationException("WAMI semantic capability example is invalid.");
}

var compatible = new ProviderCompatibility(
    cygnet.ProviderId,
    ProviderCompatibilityState.Compatible,
    cygnet.AdapterVersion,
    cygnet.ProviderVersion);

if (compatible.State != ProviderCompatibilityState.Compatible)
{
    throw new InvalidOperationException("Provider compatibility contract failed.");
}

// Provider identity must be honored rather than silently reading from the
// simulator when the requested adapter is missing or differently named.
var simulatorAdapter = new StubCurrentDataProvider("simulator");
var secondaryAdapter = new StubCurrentDataProvider("secondary");
var router = new CurrentDataProviderRouter(
    [
        new KeyValuePair<string, ICurrentDataProvider>("simulator", simulatorAdapter),
        new KeyValuePair<string, ICurrentDataProvider>("secondary", secondaryAdapter)
    ],
    "simulator");

var selected = await router.GetCurrentAsync("secondary", "WELL-101", "Pressure");
if (selected.Source != "secondary" || secondaryAdapter.CallCount != 1 ||
    simulatorAdapter.CallCount != 0)
{
    throw new InvalidOperationException("Explicit provider routing was ignored.");
}

var defaultRead = await router.GetCurrentAsync(null, "WELL-101", "Pressure");
if (defaultRead.Source != "simulator" || simulatorAdapter.CallCount != 1)
{
    throw new InvalidOperationException("Legacy default provider routing failed.");
}

try
{
    await router.GetCurrentAsync("unregistered", "WELL-101", "Pressure");
    throw new InvalidOperationException("Unknown provider silently fell back.");
}
catch (ArgumentException exception) when (exception.ParamName == "providerId")
{
}

if (simulatorAdapter.CallCount != 1 || secondaryAdapter.CallCount != 1)
{
    throw new InvalidOperationException("Unknown provider triggered an adapter read.");
}

// Repeated provider failures open a circuit rather than flooding an unhealthy
// SCADA adapter. Failed reads must never fall back to simulator data.
var broken = new FailingCurrentDataProvider();
var guarded = new CircuitBreakingCurrentDataProvider(
    broken, TimeProvider.System, 2, TimeSpan.FromMinutes(1));
for (var attempt = 0; attempt < 2; attempt++)
{
    try
    {
        await guarded.GetCurrentAsync("WELL-101", "Pressure");
        throw new InvalidOperationException("Expected adapter failure.");
    }
    catch (HttpRequestException)
    {
    }
}
try
{
    await guarded.GetCurrentAsync("WELL-101", "Pressure");
    throw new InvalidOperationException("Open circuit allowed a provider read.");
}
catch (InvalidOperationException exception)
    when (exception.Message.Contains("circuit is open", StringComparison.Ordinal))
{
}
if (broken.CallCount != 2)
{
    throw new InvalidOperationException("Circuit breaker did not suppress failed reads.");
}

Console.WriteLine("Provider-neutral adapter contract checks passed.");
return 0;

file sealed class StubCurrentDataProvider(string id) : ICurrentDataProvider
{
    public int CallCount { get; private set; }

    public ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return ValueTask.FromResult(new EngineeringValue(
            quantity, 42m, "psi", DateTimeOffset.UtcNow, "Good", id));
    }
}

file sealed class FailingCurrentDataProvider : ICurrentDataProvider
{
    public int CallCount { get; private set; }

    public ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId,
        string quantity,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        throw new HttpRequestException("Provider unavailable.");
    }
}
