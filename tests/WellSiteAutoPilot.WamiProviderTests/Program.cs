using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.Wami;
using WellSiteAutoPilot.ModuleSdk;

// Proof of a ForeSite-data + ForeSite-model + WAMI-calculation module, offline. WAMI is the deterministic fake; the
// ForeSite model route is served by a stub handler. Nothing here talks to a real ForeSite or WAMI.
static void Expect(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("ok  " + name);
}

static byte[] Model(double productivityIndex = 2, double reservoirPressure = 2500) =>
    Encoding.UTF8.GetBytes(FormattableString.Invariant(
        $"{{\"productivityIndex\":{productivityIndex},\"reservoirPressure\":{reservoirPressure},\"tubingGradient\":0.35,\"depth\":6000}}"));

var assetId = Guid.Parse("00000000-0000-4000-8000-000000000597");
var now = DateTimeOffset.Parse("2026-10-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
var clock = new FixedClock(now);

// --- Fake WAMI: deterministic, model-sensitive, refuses like the real thing is expected to.
var fake = new FakeWamiClient();
await using (var session = await fake.OpenModelAsync(new ModelArtifact("m1", "1", "fake", Model())))
{
    var inputs = new Dictionary<string, WamiQuantity> { ["wellheadPressure"] = new(300m, "psia") };
    var first = await session.CalculateAsync(new WamiCalculationRequest("r1", FakeWamiClient.NodalAnalysis, inputs));
    var again = await session.CalculateAsync(new WamiCalculationRequest("r2", FakeWamiClient.NodalAnalysis, inputs));
    Expect(first.Outputs["operatingPoint.liquidRate"] == new WamiQuantity(200m, "stb/d") &&
           first.Outputs["operatingPoint.bottomholePressure"] == new WamiQuantity(2400m, "psia"), "fake: inflow balance solves 200 stb/d at 2400 psi");
    Expect(first.Outputs["operatingPoint.liquidRate"] == again.Outputs["operatingPoint.liquidRate"], "fake: repeat calls agree");
}
Expect(fake.SessionsOpen == 0, "fake: disposing the session releases it");
await using (var session = await fake.OpenModelAsync(new ModelArtifact("m2", "1", "fake", Model(productivityIndex: 3))))
    Expect((await session.CalculateAsync(new WamiCalculationRequest("r3", FakeWamiClient.NodalAnalysis,
        new Dictionary<string, WamiQuantity> { ["wellheadPressure"] = new(300m, "psia") }))).Outputs["operatingPoint.liquidRate"].Value == 300m,
        "fake: a different model file gives a different answer");
await ExpectWami(() => fake.OpenModelAsync(new ModelArtifact("bad", "1", "fake", [1, 2, 3])).AsTask(), WamiFailureCodes.ModelRejected, WamiCallOutcome.Failed, "fake: a corrupt model is rejected");
foreach (var (mode, code) in new[] { (FakeWamiMode.Busy, WamiFailureCodes.Busy), (FakeWamiMode.Down, WamiFailureCodes.Unavailable), (FakeWamiMode.NoLicense, WamiFailureCodes.LicenseMissing) })
{
    fake.Mode = mode;
    await ExpectWami(() => fake.GetServerInfoAsync().AsTask(), code, WamiCallOutcome.NotStarted, $"fake: {mode} refuses before starting");
}

// --- ForeSite model source (read-only GET, token handshake, bounded, hash computed locally).
var modelBytes = Model();
var foreSite = new StubForeSite(modelBytes);
var source = new ForeSiteModelArtifactSource(new HttpClient(foreSite) { BaseAddress = new Uri("http://foresite.test/") });
var artifact = await source.GetCurrentModelAsync("597");
Expect(artifact.Content.SequenceEqual(modelBytes) && artifact.Ref.Revision == "41@2026-10-01T00:00:00Z" &&
       artifact.Ref.Sha256 == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes)), "ForeSite model: bytes, revision and hash come from the answer");
Expect(foreSite.Requests.Contains("GET /TokenService/CreateToken") && foreSite.Requests.Contains("GET /ModelFiles/CurrentWellModel/597") &&
       foreSite.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)), "ForeSite model: token handshake then one read-only GET");
await ExpectThrows<ArgumentException>(() => source.GetCurrentModelAsync("597/../Remove").AsTask(), "ForeSite model: non-numeric well id never reaches the URL");
foreSite.Status = HttpStatusCode.NoContent;
await ExpectModelFailure(source, "MODEL_NOT_FOUND", "ForeSite model: no current model is reported, not an empty file");
foreSite.Status = HttpStatusCode.OK;
foreSite.Body = """{"Id":41,"Contents":"not base64!"}""";
await ExpectModelFailure(source, "MODEL_SOURCE_INVALID", "ForeSite model: bad contents are refused");
foreSite.Body = "{\"Id\":41,\"Contents\":\"" + Convert.ToBase64String(new byte[ModelArtifact.MaximumBytes + 1]) + "\"}";
await ExpectModelFailure(source, "MODEL_TOO_LARGE", "ForeSite model: oversize model is refused");

// --- Multi-provider coordinator: data:foresite-a, model:foresite-b (same external id, different source), calc:wami.
var dependencies = new[]
{
    new ProviderDependency("data", DependencyCapability.CurrentData, "foresite"),
    new ProviderDependency("model", DependencyCapability.ModelArtifact, "foresite"),
    new ProviderDependency("calc", DependencyCapability.Calculation, "wami")
};
var bindings = new[]
{
    new DependencyBinding("data", "foresite", "foresite-a", 3, assetId, "597"),
    new DependencyBinding("model", "foresite", "foresite-b", 8, assetId, "597"),
    new DependencyBinding("calc", "wami", "wami-1", 2, assetId, null)
};
CompositionPlan Plan(IReadOnlyList<DependencyBinding>? use = null, TimeSpan? timeout = null) => new(
    "well-analysis", dependencies, use ?? bindings, "model", "calc", FakeWamiClient.NodalAnalysis,
    [new CalculationInput("data", "WellheadPressure", "wellheadPressure", "psia")],
    timeout ?? TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10), MaximumBusyAttempts: 2);

var data = new ScriptedData() { ["597/WellheadPressure"] = new("WellheadPressure", 300m, "psia", now.AddMinutes(-1), "Good", "foresite-a") };
var wami = new FakeWamiClient();
var live = new StubForeSite(modelBytes);
var registry = new ProviderRegistry()
    .AddCurrentData("foresite-a", data)
    .AddModelSource("foresite-b", new ForeSiteModelArtifactSource(new HttpClient(live) { BaseAddress = new Uri("http://foresite.test/") }))
    .AddCalculation("wami-1", wami);
var authorizer = new Authorizer();
var coordinator = new MultiProviderCoordinator(registry, authorizer, clock);

var run = await coordinator.RunAsync(Plan(), null, CancellationToken.None);
var rate = run.Outputs.Single(output => output.Quantity == "operatingPoint.liquidRate");
Expect(run.Status == CompositionStatus.Completed && rate.Value == 200m && rate.Unit == "stb/d", "scenario: ForeSite data + model feed WAMI and return 200 stb/d");
Expect(rate.Source.StartsWith("wami:wami-1@2:nodal-analysis:", StringComparison.Ordinal) && run.Observed!.Model.Sha256.StartsWith(rate.Source[^12..], StringComparison.Ordinal),
    "scenario: output provenance names the WAMI connection revision and model hash");
Expect(run.Audit.Select(item => item.Step).SequenceEqual(["validate", "authorize", "read", "model", "calculate", "complete"]) &&
       run.Audit.All(item => !item.Detail.Contains("psi\"", StringComparison.Ordinal)), "scenario: audit records every step in order");
Expect(wami.SessionsOpen == 0 && authorizer.Asked.Count == 3, "scenario: session released; each role authorised independently");

var module = new ThresholdModule();
var moduleInputs = run.Outputs.Select(output => new LogicInputValue(output.Quantity, assetId, output.Quantity, output.Value!.Value, output.Unit, output.TimestampUtc, output.Quality, output.Source)).ToArray();
var moduleResult = await module.ExecuteAsync(new LogicModuleExecutionContext(
    Guid.NewGuid(), module.Identity, "wami-poc", now, "{}", [new LogicAssetContext("well", assetId, "{}")], moduleInputs));
Expect(moduleResult.FailureCode is null && moduleResult.Recommendations.Single().Code == "LOW_RATE" && moduleResult.ControlIntents.Count == 0,
    "scenario: module turns the WAMI result into a recommendation, no control intent");

// Pinned re-run: same pins pass; each change stops before WAMI is called.
var pins = run.Observed;
var rerun = await coordinator.RunAsync(Plan(), pins, CancellationToken.None);
Expect(rerun.Status == CompositionStatus.Completed, "pinned re-run with unchanged bindings and model completes");

async Task<CompositionResult> Stopped(string label, CompositionPlan plan, CompositionPins? pin, string code, CompositionStatus status = CompositionStatus.Failed)
{
    var before = wami.SessionsOpened;
    var result = await coordinator.RunAsync(plan, pin, CancellationToken.None);
    Expect(result.Status == status && result.FailureCode == code && result.Outputs.Count == 0 && wami.SessionsOpened == before, label);
    return result;
}
await Stopped("pin: changed connection revision", Plan([bindings[0], bindings[1], bindings[2] with { ConnectionRevision = 9 }]), pins, "DEPENDENCY_PIN_MISMATCH");
await Stopped("pin: different connection for a role", Plan([bindings[0] with { ConnectionId = "foresite-z" }, bindings[1], bindings[2]]), pins, "DEPENDENCY_PIN_MISMATCH");
await Stopped("binding: wrong adapter for the calc role", Plan([bindings[0], bindings[1], bindings[2] with { AdapterKey = "foresite" }]), null, "DEPENDENCY_ADAPTER_MISMATCH");
await Stopped("binding: roles on different logical assets", Plan([bindings[0], bindings[1], bindings[2] with { LogicalAssetId = Guid.NewGuid() }]), null, "DEPENDENCY_ASSET_MISMATCH");
await Stopped("binding: role missing", Plan([bindings[0], bindings[2]]), null, "DEPENDENCY_SET_INVALID");
await Stopped("binding: provider not registered", Plan([bindings[0], bindings[1], bindings[2] with { ConnectionId = "wami-unknown" }]), null, "DEPENDENCY_UNAVAILABLE");
authorizer.Denied.Add("calc");
await Stopped("authority: denying only the WAMI role stops the run", Plan(), null, "DEPENDENCY_DENIED");
authorizer.Denied.Clear();
authorizer.Denied.Add("data");
await Stopped("authority: denying only the data role stops the run", Plan(), null, "DEPENDENCY_DENIED");
authorizer.Denied.Clear();

live.Body = null;
live.Bytes = Model(productivityIndex: 3);
await Stopped("model: a changed model file stops a pinned run", Plan(), pins, "MODEL_PIN_MISMATCH");
live.Bytes = modelBytes;

data["597/WellheadPressure"] = data["597/WellheadPressure"] with { TimestampUtc = now.AddHours(-2) };
await Stopped("input: stale reading", Plan(), null, "INPUT_STALE");
data["597/WellheadPressure"] = data["597/WellheadPressure"] with { TimestampUtc = now, Quality = "Uncertain" };
await Stopped("input: poor quality reading", Plan(), null, "INPUT_QUALITY");
data["597/WellheadPressure"] = data["597/WellheadPressure"] with { Quality = "Good", Unit = "kPa" };
await Stopped("input: unit is checked, never guessed", Plan(), null, "INPUT_UNIT");
data["597/WellheadPressure"] = data["597/WellheadPressure"] with { Unit = "psia", Value = null };
await Stopped("input: a missing value is not a zero", Plan(), null, "INPUT_MISSING");
data["597/WellheadPressure"] = data["597/WellheadPressure"] with { Value = 300m };

wami.Mode = FakeWamiMode.Busy;
var busy = await Stopped("WAMI busy: refused twice, no result", Plan(), null, WamiFailureCodes.Busy);
Expect(busy.Audit.Count(item => item.Step == "calculate") == 2, "WAMI busy: one bounded repeat, both recorded");
wami.Mode = FakeWamiMode.NoLicense;
await Stopped("WAMI licence missing", Plan(), null, WamiFailureCodes.LicenseMissing);
wami.Mode = FakeWamiMode.Down;
await Stopped("WAMI down", Plan(), null, WamiFailureCodes.Unavailable);

wami.Mode = FakeWamiMode.Hangs;
var dispatchedBefore = wami.CalculationsDispatched;
var hung = await coordinator.RunAsync(Plan(timeout: TimeSpan.FromMilliseconds(200)), null, CancellationToken.None);
Expect(hung.Status == CompositionStatus.Ambiguous && hung.FailureCode == WamiFailureCodes.Timeout && hung.Outputs.Count == 0 &&
       wami.CalculationsDispatched == dispatchedBefore + 1 && wami.SessionsOpen == 0,
    "timeout after dispatch is ambiguous, is not repeated and releases the session");

using (var cancel = new CancellationTokenSource())
{
    var pending = coordinator.RunAsync(Plan(), null, cancel.Token);
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (wami.CalculationsDispatched == dispatchedBefore + 1 && DateTime.UtcNow < deadline) await Task.Delay(10);
    cancel.Cancel();
    var cancelled = await pending;
    Expect(cancelled.Status == CompositionStatus.Ambiguous && cancelled.FailureCode == "CALCULATION_CANCELLED", "cancel after dispatch is ambiguous, not 'cancelled'");
}
wami.Mode = FakeWamiMode.Normal;
using (var already = new CancellationTokenSource())
{
    already.Cancel();
    var before = wami.CalculationsDispatched;
    var result = await coordinator.RunAsync(Plan(), null, already.Token);
    Expect(result.Status == CompositionStatus.Cancelled && wami.CalculationsDispatched == before, "cancel before dispatch is a clean cancel");
}

// --- Descriptor: WAMI declares only a calculation capability, no discovery.
Expect(!WamiAdapter.Descriptor.CanDiscover && WamiAdapter.Descriptor.Capabilities.Single().Kind == ProviderCapabilityKind.Calculation,
    "descriptor: WAMI is a calculation provider with no asset discovery");

Console.WriteLine("All WAMI provider checks passed.");

static async Task ExpectWami(Func<Task> call, string code, WamiCallOutcome outcome, string name)
{
    try { await call(); }
    catch (WamiException error) { Expect(error.Code == code && error.Outcome == outcome, name); return; }
    throw new InvalidOperationException("FAILED (no exception): " + name);
}

static async Task ExpectThrows<T>(Func<Task> call, string name) where T : Exception
{
    try { await call(); }
    catch (T) { Expect(true, name); return; }
    throw new InvalidOperationException("FAILED (no exception): " + name);
}

static async Task ExpectModelFailure(ForeSiteModelArtifactSource source, string code, string name)
{
    try { await source.GetCurrentModelAsync("597"); }
    catch (ModelSourceException error) { Expect(error.Code == code, name); return; }
    throw new InvalidOperationException("FAILED (no exception): " + name);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class Authorizer : IDependencyAuthorizer
{
    public HashSet<string> Denied { get; } = [];
    public List<string> Asked { get; } = [];

    public ValueTask<bool> IsAllowedAsync(DependencyBinding binding, DependencyCapability capability, CancellationToken cancellationToken)
    {
        Asked.Add(binding.Role);
        return ValueTask.FromResult(!Denied.Contains(binding.Role));
    }
}

internal sealed class ScriptedData : ICurrentDataProvider
{
    private readonly Dictionary<string, EngineeringValue> _values = [];

    public EngineeringValue this[string key]
    {
        get => _values[key];
        set => _values[key] = value;
    }

    public ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _values.TryGetValue($"{assetExternalId}/{quantity}", out var value)
            ? ValueTask.FromResult(value)
            : throw new InvalidOperationException("No such point.");
    }
}

internal sealed class StubForeSite(byte[] bytes) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string? Body { get; set; }
    public byte[] Bytes { get; set; } = bytes;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add($"{request.Method} {path}");
        if (path.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal))
        {
            var token = new HttpResponseMessage(HttpStatusCode.OK);
            token.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc; Path=/");
            return Task.FromResult(token);
        }

        if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
        var json = Body ?? "{\"Id\":41,\"ApplicableDate\":\"2026-10-01T00:00:00Z\",\"WellId\":597,\"Contents\":\"" + Convert.ToBase64String(Bytes) + "\"}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}

/// <summary>Example module: compares the calculated rate with a threshold. It never talks to a provider.</summary>
internal sealed class ThresholdModule : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } = new("wami-poc.low-rate", "0.1.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var rate = context.Inputs.Single(input => input.Quantity == "operatingPoint.liquidRate");
        var recommendations = rate.Value < 250m
            ? new[] { new RecommendationIntentDraft("LOW_RATE", rate.AssetId, "Operating point below target", $"WAMI operating point {rate.Value} {rate.Unit}.") }
            : [];
        return ValueTask.FromResult(new LogicModuleExecutionResult("EVALUATED", [], recommendations, [], new Dictionary<string, string>()));
    }
}
