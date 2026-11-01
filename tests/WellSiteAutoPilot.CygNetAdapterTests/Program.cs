using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.IntegrationGateway.Adapters;
using WellSiteAutoPilot.ModuleSdk;

const string Account = @"CORP\svc-wsa";
var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
var sim = new SimulatedCygNetClient(clock);
var settings = new Dictionary<string, string?> { ["domain"] = "27212", ["siteService"] = "CYG_DEMO.UIS" };

// Descriptor and registration
var descriptor = CygNetAdapter.Descriptor;
Expect(descriptor.Key == "cygnet" && descriptor.CanDiscover, "descriptor is cygnet with discovery");
Expect(descriptor.Capabilities.Select(c => c.Kind).Order().SequenceEqual(
    new[] { ProviderCapabilityKind.AssetDiscovery, ProviderCapabilityKind.CurrentData }.Order()),
    "capabilities are exactly discovery and current data");
Expect(new AdapterCatalog().Find("cygnet") == descriptor, "the Gateway catalog lists CygNet");
Expect(AdapterSettingsValidator.Validate(descriptor, settings).Count == 0, "valid settings pass");
Expect(AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?>()).Count == 2, "domain and site.service are required");
Expect(AdapterReadRules.Find("cygnet")?.ResourceKind == "cygnet.facility" && AdapterReadRules.Find("nope") is null, "read rules");
var cygNetRule = AdapterReadRules.Find("cygnet")!;
Expect(cygNetRule.AllowsCurrentRead("MotorAmps", null, null) && !cygNetRule.AllowsCurrentRead("MotorAmps", "structured.current", null) &&
    !cygNetRule.AllowsCurrentRead("SurfaceCards", "structured.current", "foresite.surface-cards/v1") && !cygNetRule.AllowsCurrentRead(null, null, null),
    "CygNet serves only its scalar quantities, with no capability or schema");
var foreSiteRule = AdapterReadRules.Find("foresite")!;
Expect(foreSiteRule.AllowsCurrentRead("PumpFillage", null, null) &&
    foreSiteRule.AllowsCurrentRead("SurfaceCards", "structured.current", "foresite.surface-cards/v1") &&
    !foreSiteRule.AllowsCurrentRead("SurfaceCards", null, null) && !foreSiteRule.AllowsCurrentRead("SurfaceCards", "structured.current", "other/v1") &&
    !foreSiteRule.AllowsCurrentRead("MotorAmps", null, null),
    "ForeSite keeps its scalar fillage reads and the one structured card read");

// Settings parsing
var connection = CygNetAdapter.ReadConnection(settings, out var problem)!;
Expect(connection is { Domain: 27212, Site: "CYG_DEMO", Service: "UIS", FacilityFilter: null } && problem is null, "connection parsed");
Expect(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = "x", ["siteService"] = "A.B" }, out _) is null, "bad domain");
Expect(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = "1", ["siteService"] = "AB" }, out _) is null, "bad site.service");
Expect(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = "1", ["siteService"] = "A.B", ["facilityFilter"] = "W;DROP" }, out _) is null, "bad filter");
Expect(CygNetAdapter.Matches("well_*", "WELL_001") && CygNetAdapter.Matches("RPOC_00?1", "RPOC_0001") && !CygNetAdapter.Matches("WELL_*", "TANK_01"), "wildcards");
var mapped = CygNetAdapter.ReadConnection(new Dictionary<string, string?>
    { ["domain"] = "1", ["siteService"] = "A.B", ["pointMap"] = "motoramps=MTRAMPS|A; PumpFillage=RRLPFILCUR|%" }, out _);
Expect(mapped?.PointMap is { Count: 2 } map && map["MotorAmps"] == new CygNetPointMapping("MTRAMPS", "A") && map["PumpFillage"].Udc == "RRLPFILCUR",
    "point mapping is parsed per connection");
foreach (var bad in new[] { "Bogus=X|A", "MotorAmps=X", "MotorAmps=X Y|A", "MotorAmps=X|A;MotorAmps=Y|A", "MotorAmps=X|" })
    Expect(CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = "1", ["siteService"] = "A.B", ["pointMap"] = bad }, out var why) is null && why is not null,
        "bad point mapping is refused: " + bad);

// Connection test
Expect((await CygNetAdapter.CheckAsync(sim, connection, Account)).Outcome == AdapterTestOutcomes.Healthy, "simulated check is healthy");
var unconfigured = await CygNetAdapter.CheckAsync(new CygNetUnconfiguredClient(), connection, Account);
Expect(unconfigured.Outcome == AdapterTestOutcomes.NeedsAttention, "unconfigured backend needs attention");
var leaky = await CygNetAdapter.CheckAsync(new FailingClient(new InvalidOperationException("SECRET-TEXT")), connection, Account);
Expect(leaky.Outcome == AdapterTestOutcomes.NeedsAttention && !leaky.Message.Contains("SECRET", StringComparison.Ordinal), "vendor text is not shown");
var refused = await CygNetAdapter.CheckAsync(new FailingClient(new UnauthorizedAccessException("SECRET")), connection, Account);
Expect(refused.Message.Contains("licence", StringComparison.Ordinal) && !refused.Message.Contains("SECRET", StringComparison.Ordinal), "refused account");
var tester = new AdapterConnectionTester(null!, null!, sim);
Expect((await tester.TestAsync(descriptor, settings, CancellationToken.None)).Outcome == AdapterTestOutcomes.Healthy, "gateway tester routes cygnet");

// Discovery
var discovered = await CygNetAdapter.DiscoverAsync(sim, connection, Account);
Expect(discovered.Complete && discovered.Items.Count == 8, "simulated discovery lists every facility");
var well = discovered.Items.Single(i => i.DisplayName == "WELL_002");
Expect(well.ExternalId == "CYG_DEMO.UIS::WELL_002" && well.ProviderType == "cygnet.facility" &&
       well.Metadata["category"] == "Pad A" && well.Metadata["facilityType"] == "WELL" && well.Metadata["domain"] == "27212",
    "facility mapped with tag, type and grouping facts");
var filtered = await CygNetAdapter.DiscoverAsync(sim, connection with { FacilityFilter = "WELL_*" }, Account);
Expect(filtered.Items.Count == 6 && filtered.Items.All(i => i.DisplayName.StartsWith("WELL_", StringComparison.Ordinal)), "filter narrows discovery");
var viaGateway = await new AdapterDiscoverer(null!, null!, sim).DiscoverAsync(descriptor, settings, CancellationToken.None);
Expect(viaGateway.Items.Count == 8, "gateway discoverer routes cygnet");

var truncated = CygNetAdapter.MapFacilities(connection, new CygNetFacilityList([new("A1"), new("A2")], Truncated: true), Account);
Expect(!truncated.Complete && truncated.Items.Count == 2 && truncated.Warnings.Any(w => w.Contains("more facilities", StringComparison.Ordinal)),
    "truncation is reported, never silent");
var skipped = CygNetAdapter.MapFacilities(connection, new CygNetFacilityList([new("A1"), new(" "), new("B::C")]), Account);
Expect(!skipped.Complete && skipped.Items.Count == 1, "unusable ids are skipped and flagged");
var duplicate = CygNetAdapter.MapFacilities(connection, new CygNetFacilityList([new("A1"), new("a1")]), Account);
Expect(!duplicate.Complete && duplicate.Items.Count == 0, "duplicates fail closed");
var down = await CygNetAdapter.DiscoverAsync(new FailingClient(new CygNetUnavailableException("No backend.")), connection, Account);
Expect(!down.Complete && down.Items.Count == 0 && down.Warnings.Count > 0, "a failed list is an incomplete result");

// Reads
var tag = "CYG_DEMO.UIS::WELL_001";
var fillage = await CygNetAdapter.ReadCurrentAsync(sim, connection, tag, "PumpFillage");
Expect(fillage.Value == 82.5m && fillage.Unit == "%" && fillage.Quality == "Good" && fillage.Source == SimulatedCygNetClient.Source &&
       fillage.TimestampUtc == clock.GetUtcNow().AddSeconds(-5), "healthy fillage read");
foreach (var quantity in AdapterReadRules.CygNetQuantities)
    Expect((await CygNetAdapter.ReadCurrentAsync(sim, connection, tag, quantity)).Value is not null, "every declared quantity reads: " + quantity);
var stale = await CygNetAdapter.ReadCurrentAsync(sim, connection, "CYG_DEMO.UIS::WELL_005", "PumpFillage");
Expect(stale.Quality == "Uncertain" && stale.TimestampUtc == clock.GetUtcNow().AddSeconds(-600), "stale uncertain read keeps its quality and time");
await ExpectCode("CYGNET_QUANTITY_UNSUPPORTED", () => CygNetAdapter.ReadCurrentAsync(sim, connection, tag, "SurfaceCards"));
await ExpectCode("CYGNET_FACILITY_INVALID", () => CygNetAdapter.ReadCurrentAsync(sim, connection, "OTHER.UIS::WELL_001", "PumpFillage"));
await ExpectCode("CYGNET_FACILITY_INVALID", () => CygNetAdapter.ReadCurrentAsync(sim, connection, "CYG_DEMO.UIS::", "PumpFillage"));
await ExpectCode("CYGNET_POINT_NOT_FOUND", () => CygNetAdapter.ReadCurrentAsync(sim, connection, "CYG_DEMO.UIS::TANK_01", "PumpFillage"));
await ExpectCode("CYGNET_UNAVAILABLE", () => CygNetAdapter.ReadCurrentAsync(new FailingClient(new CygNetUnavailableException("x")), connection, tag, "PumpFillage"));
try
{
    await CygNetAdapter.ReadCurrentAsync(new FailingClient(new InvalidOperationException("SECRET-TEXT")), connection, tag, "PumpFillage");
    throw new InvalidOperationException("expected failure");
}
catch (WellSiteAutoPilotException error)
{
    Expect(error.Code == "CYGNET_READ_FAILED" && !error.SafeDetail.Contains("SECRET", StringComparison.Ordinal), "unexplained failures are redacted");
}

// Sample modules against simulated values
var assetId = Guid.NewGuid();
async Task<LogicModuleExecutionResult> Run(ILogicModuleV1 module, string facility, string json, params (string Id, string Quantity)[] inputs)
{
    var values = new List<LogicInputValue>();
    foreach (var (id, quantity) in inputs)
    {
        var v = await sim.ReadCurrentAsync(connection, facility, quantity, CancellationToken.None);
        values.Add(new LogicInputValue(id, assetId, quantity, v.Value!.Value, v.Unit, v.TimestampUtc, v.Quality, v.Source));
    }
    return await module.ExecuteAsync(new LogicModuleExecutionContext(Guid.NewGuid(), module.Identity, "test", clock.GetUtcNow(), json,
        [new LogicAssetContext("well", assetId, "{}")], values));
}

var fillageModule = new Weatherford.CygNet.PumpFillageLowAlert.EntryPoint();
var low = await Run(fillageModule, "WELL_002", "{}", ("pump-fillage", "PumpFillage"));
Expect(low.OutcomeCode == "PUMP_FILLAGE_LOW" && low.Recommendations.Count == 1 && low.FailureCode is null, "WELL_002 triggers low fillage");
var healthy = await Run(fillageModule, "WELL_001", "{}", ("pump-fillage", "PumpFillage"));
Expect(healthy.OutcomeCode == "PUMP_FILLAGE_NORMAL" && healthy.Recommendations.Count == 0, "WELL_001 is normal");
var strict = await Run(fillageModule, "WELL_001", "{\"lowFillagePercent\":90}", ("pump-fillage", "PumpFillage"));
Expect(strict.OutcomeCode == "PUMP_FILLAGE_LOW", "threshold parameter is honoured");
var badParameter = await Run(fillageModule, "WELL_001", "{\"lowFillagePercent\":\"x\"}", ("pump-fillage", "PumpFillage"));
Expect(badParameter.FailureCode == "PARAMETERS_INVALID", "bad parameter fails closed");

var pressure = new Weatherford.CygNet.PressureDifferentialCheck.EntryPoint();
var pressureInputs = new[] { ("casing-pressure", "CasingPressure"), ("tubing-pressure", "TubingPressure") };
Expect((await Run(pressure, "WELL_003", "{}", pressureInputs)).OutcomeCode == "PRESSURE_DIFFERENTIAL_HIGH", "WELL_003 differential is high");
Expect((await Run(pressure, "WELL_001", "{}", pressureInputs)).OutcomeCode == "PRESSURE_DIFFERENTIAL_NORMAL", "WELL_001 differential is normal");

var motor = new Weatherford.CygNet.MotorLoadCheck.EntryPoint();
var motorInputs = new[] { ("motor-amps", "MotorAmps"), ("rod-load", "RodLoad") };
var heavy = await Run(motor, "WELL_004", "{}", motorInputs);
Expect(heavy.OutcomeCode == "MOTOR_LOAD_HIGH" && heavy.Recommendations.Count == 1, "WELL_004 motor load is high");
Expect((await Run(motor, "WELL_001", "{}", motorInputs)).OutcomeCode == "MOTOR_LOAD_NORMAL", "WELL_001 load is normal");

// (1) Point values: blank, text and flagged records say why
Expect(CygNetValues.ParseNumber("           99.67") == 99.67m && CygNetValues.ParseNumber("1E3") == 1000m, "padded and exponent numbers parse");
Expect(Throws(() => CygNetValues.ParseNumber("")).Code == CygNetValues.NoValueCode && Throws(() => CygNetValues.ParseNumber("   ")).Code == CygNetValues.NoValueCode,
    "a blank record means the point has no value yet");
var text = Throws(() => CygNetValues.ParseNumber("Running"));
Expect(text.Code == CygNetValues.NotNumericCode && text.SafeDetail.Contains("point mapping", StringComparison.Ordinal), "text values name the remedy");
Expect(CygNetValues.Quality("Initialized, Updated") == "Good" && CygNetValues.Quality("Initialized, Updated, StringData") == "Good" &&
       CygNetValues.Quality("Initialized, Updated, ConfigurableBit1") == "Uncertain" && CygNetValues.Quality("Updated, Unreliable") == "Bad" &&
       CygNetValues.Quality(null) == "Good", "status flags map to quality");
await ExpectCode(CygNetValues.NoValueCode, () => CygNetAdapter.ReadCurrentAsync(sim, connection, "CYG_DEMO.UIS::WELL_006", "PumpFillage"));

// (2) Freshness diagnostics in the test and discovery results
var fresh = await CygNetAdapter.CheckAsync(sim, connection, Account, clock, CancellationToken.None);
Expect(fresh.Outcome == AdapterTestOutcomes.Healthy && fresh.Message.Contains("Sampled 3 facility", StringComparison.Ordinal) &&
       fresh.Message.Contains("PumpFillage 5 s to 10 min old", StringComparison.Ordinal) && !fresh.Message.Contains("maximum data age", StringComparison.Ordinal),
    "fresh data reports its age and needs no advice: " + fresh.Message);
var staleSim = new SimulatedCygNetClient(clock, SimulatedCygNetProfile.DemoStale);
var old = await CygNetAdapter.CheckAsync(staleSim, connection, Account, clock, CancellationToken.None);
Expect(old.Outcome == AdapterTestOutcomes.Healthy && old.Message.Contains("6.0 years old", StringComparison.Ordinal) &&
       old.Message.Contains("maximum data age must be at least", StringComparison.Ordinal), "stale data is reported with guidance: " + old.Message);
var found = await CygNetAdapter.DiscoverAsync(staleSim, connection, Account, clock, CancellationToken.None);
Expect(found.Complete && found.UnavailableFields is { Count: 0 } && found.Warnings.Any(w => w.StartsWith("Data freshness.", StringComparison.Ordinal)),
    "discovery stays complete and carries the freshness note without marking facts unavailable");
Expect(CygNetFreshness.Pick(["A", "B", "C", "D", "E"]).SequenceEqual(["A", "C", "E"]) && CygNetFreshness.Pick(["A", "B"]).Count == 2, "samples are spread over the list");
var noValue = await CygNetAdapter.CheckAsync(sim, connection with { FacilityFilter = "WELL_006" }, Account, clock, CancellationToken.None);
Expect(noValue.Outcome == AdapterTestOutcomes.Healthy && noValue.Message.Contains("no value yet", StringComparison.Ordinal), "points without values are reported, not fatal");
Expect(CygNetValues.FormatAge(TimeSpan.FromSeconds(45)) == "45 s" && CygNetValues.FormatAge(TimeSpan.FromMinutes(16)) == "16 min" &&
       CygNetValues.FormatAge(TimeSpan.FromDays(3)) == "3 days", "ages are short");

// (3) Identities across site.service, domain and case
var otherService = connection with { Site = "OTHER", Service = "UIS" };
var inOther = CygNetAdapter.MapFacilities(otherService, new CygNetFacilityList([new("WELL_001")]), Account);
var inDemo = CygNetAdapter.MapFacilities(connection, new CygNetFacilityList([new("WELL_001")]), Account);
Expect(inOther.Items[0].ExternalId == "OTHER.UIS::WELL_001" && inDemo.Items[0].ExternalId == "CYG_DEMO.UIS::WELL_001",
    "the same facility id in two services has two external ids");
await ExpectCode("CYGNET_FACILITY_INVALID", () => CygNetAdapter.ReadCurrentAsync(sim, otherService, inDemo.Items[0].ExternalId, "PumpFillage"));
Expect(CygNetAdapter.MapFacilities(connection, new CygNetFacilityList([new("Well_1"), new("WELL_1")]), Account).Items.Count == 0,
    "ids that differ only by case are treated as a collision");
Expect(CygNetAdapter.MapFacilities(connection with { Domain = 1 }, new CygNetFacilityList([new("WELL_001")]), Account).Items[0].Metadata["domain"] == "1",
    "the domain is a fact, so two domains can be told apart");

// (4) Point mapping validation through the connection test
var wrongMap = connection with { PointMap = new Dictionary<string, CygNetPointMapping> { ["MotorAmps"] = new("NOSUCH", "A") } };
var mapCheck = await CygNetAdapter.CheckAsync(new MissingPointClient(sim, "MotorAmps"), wrongMap, Account, clock, CancellationToken.None);
Expect(mapCheck.Outcome == AdapterTestOutcomes.NeedsAttention && mapCheck.Message.Contains("point mapping names points that were not found", StringComparison.Ordinal) &&
       mapCheck.Message.Contains("MotorAmps", StringComparison.Ordinal), "a mapped point that exists nowhere needs attention: " + mapCheck.Message);
var defaultGap = await CygNetAdapter.CheckAsync(new MissingPointClient(sim, "MotorAmps"), connection, Account, clock, CancellationToken.None);
Expect(defaultGap.Outcome == AdapterTestOutcomes.Healthy && defaultGap.Message.Contains("MotorAmps point not found", StringComparison.Ordinal),
    "a default point missing on the sample is reported but not an error");
var unmappedCheck = await CygNetAdapter.CheckAsync(new UnmappedClient(sim, "MotorAmps"), connection, Account, clock, CancellationToken.None);
Expect(unmappedCheck.Message.Contains("MotorAmps not mapped", StringComparison.Ordinal), "an unmapped quantity is named");

// (5) Bounded calls: a hung CygNet holds a few slots, then calls fail fast
var gate = new CygNetCallGate(slots: 2);
using var release = new ManualResetEventSlim();
Task<int> Hang() => gate.RunAsync("27212/CYG_DEMO.UIS", () => { release.Wait(); return 1; }, TimeSpan.FromMilliseconds(100), CancellationToken.None);
for (var i = 0; i < 2; i++)
    await Expect_Throws<TimeoutException>(Hang);
await Expect_Throws<CygNetBusyException>(Hang);
Expect(await gate.RunAsync("27212/OTHER.UIS", () => 7, TimeSpan.FromSeconds(5), CancellationToken.None) == 7, "another service is not starved by a hung one");
release.Set();
await Task.Delay(100);
Expect(await gate.RunAsync("27212/CYG_DEMO.UIS", () => 2, TimeSpan.FromSeconds(5), CancellationToken.None) == 2, "slots free up when the hung calls finish");

Console.WriteLine("CygNet adapter checks passed.");
return 0;

static WellSiteAutoPilotException Throws(Action action)
{
    try
    {
        action();
    }
    catch (WellSiteAutoPilotException error)
    {
        return error;
    }
    throw new InvalidOperationException("CygNet adapter check failed: expected a failure.");
}

static async Task Expect_Throws<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException("CygNet adapter check failed: expected " + typeof(T).Name + ".");
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("CygNet adapter check failed: " + message + ".");
}

static async Task ExpectCode(string code, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (WellSiteAutoPilotException error)
    {
        Expect(error.Code == code, $"expected {code}, got {error.Code}");
        return;
    }
    throw new InvalidOperationException($"CygNet adapter check failed: expected {code}.");
}

sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

sealed class FailingClient(Exception error) : ICygNetClient
{
    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<string>>(error);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<CygNetFacilityList>(error);

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) =>
        Task.FromException<EngineeringValue>(error);
}


// Reports one quantity's point as missing on every facility, as CygNet does for a UDC the facility lacks.
sealed class MissingPointClient(ICygNetClient inner, string quantity) : ICygNetClient
{
    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => inner.CheckAsync(connection, cancellationToken);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) => inner.ListFacilitiesAsync(connection, cancellationToken);

    public Task<EngineeringValue> ReadCurrentAsync(CygNetConnection connection, string facilityId, string q, CancellationToken cancellationToken) =>
        q == quantity
            ? Task.FromException<EngineeringValue>(new CygNetReadException("CYGNET_POINT_NOT_FOUND", "No such point."))
            : inner.ReadCurrentAsync(connection, facilityId, q, cancellationToken);
}

sealed class UnmappedClient(ICygNetClient inner, string quantity) : ICygNetClient
{
    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => inner.CheckAsync(connection, cancellationToken);

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) => inner.ListFacilitiesAsync(connection, cancellationToken);

    public Task<EngineeringValue> ReadCurrentAsync(CygNetConnection connection, string facilityId, string q, CancellationToken cancellationToken) =>
        q == quantity
            ? Task.FromException<EngineeringValue>(new CygNetReadException("CYGNET_QUANTITY_UNSUPPORTED", "Not mapped."))
            : inner.ReadCurrentAsync(connection, facilityId, q, cancellationToken);
}
