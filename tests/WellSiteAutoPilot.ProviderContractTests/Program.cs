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

// Open circuits fail closed without invoking an unhealthy adapter.
var failing = new RecoveringCurrentDataProvider();
var clock = new ManualProviderClock(DateTimeOffset.UtcNow);
var breaker = new CircuitBreakingCurrentDataProvider(
    failing, clock, 2, TimeSpan.FromMinutes(1));
for (var attempt = 0; attempt < 2; attempt++)
{
    try
    {
        await breaker.GetCurrentAsync("WELL-101", "Pressure");
        throw new InvalidOperationException("Expected provider failure.");
    }
    catch (HttpRequestException)
    {
    }
}
try
{
    await breaker.GetCurrentAsync("WELL-101", "Pressure");
    throw new InvalidOperationException("Open circuit allowed a read.");
}
catch (InvalidOperationException exception)
    when (exception.Message.Contains("circuit is open", StringComparison.Ordinal))
{
}
if (failing.CallCount != 2)
    throw new InvalidOperationException("Open circuit contacted provider.");

clock.Advance(TimeSpan.FromMinutes(1));
failing.Fail = false;
var recovered = await breaker.GetCurrentAsync("WELL-101", "Pressure");
if (recovered.Source != "recovered" || failing.CallCount != 3)
    throw new InvalidOperationException("Half-open recovery probe failed.");
await breaker.GetCurrentAsync("WELL-101", "Pressure");
if (failing.CallCount != 4)
    throw new InvalidOperationException("Successful recovery did not close circuit.");

// A failed recovery probe must reopen the circuit for another full delay.
var retryProvider = new RecoveringCurrentDataProvider();
var retryClock = new ManualProviderClock(DateTimeOffset.UtcNow);
var retryBreaker = new CircuitBreakingCurrentDataProvider(
    retryProvider, retryClock, 1, TimeSpan.FromSeconds(10));
try
{
    await retryBreaker.GetCurrentAsync("WELL-101", "Pressure");
    throw new InvalidOperationException("Initial failure unexpectedly succeeded.");
}
catch (HttpRequestException)
{
}
retryClock.Advance(TimeSpan.FromSeconds(10));
try
{
    await retryBreaker.GetCurrentAsync("WELL-101", "Pressure");
    throw new InvalidOperationException("Failed recovery probe unexpectedly succeeded.");
}
catch (HttpRequestException)
{
}
if (retryProvider.CallCount != 2)
    throw new InvalidOperationException("Recovery probe was not attempted.");
try
{
    await retryBreaker.GetCurrentAsync("WELL-101", "Pressure");
    throw new InvalidOperationException("Failed probe did not reopen circuit.");
}
catch (InvalidOperationException exception)
    when (exception.Message.Contains("circuit is open", StringComparison.Ordinal))
{
}
if (retryProvider.CallCount != 2)
    throw new InvalidOperationException("Reopened circuit contacted provider.");

// Cancellation is not a provider failure and must not trip the breaker.
var canceledProvider = new RecoveringCurrentDataProvider();
var cancellationBreaker = new CircuitBreakingCurrentDataProvider(
    canceledProvider, TimeProvider.System, 1);
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try
    {
        await cancellationBreaker.GetCurrentAsync("WELL-101", "Pressure", cancellation.Token);
        throw new InvalidOperationException("Canceled provider call succeeded.");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
}
canceledProvider.Fail = false;
await cancellationBreaker.GetCurrentAsync("WELL-101", "Pressure");
if (canceledProvider.CallCount != 1)
    throw new InvalidOperationException("Cancellation incorrectly opened the circuit.");

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

file sealed class ManualProviderClock(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _now = initial;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

file sealed class RecoveringCurrentDataProvider : ICurrentDataProvider
{
    public bool Fail { get; set; } = true;
    public int CallCount { get; private set; }
    public ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId, string quantity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        if (Fail) throw new HttpRequestException("Provider unavailable.");
        return ValueTask.FromResult(new EngineeringValue(
            quantity, 42m, "psi", DateTimeOffset.UtcNow, "Good", "recovered"));
    }
}
