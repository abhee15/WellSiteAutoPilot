using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

var checks = 0;
var settings = new Dictionary<string, string?> { ["domain"] = "123", ["site"] = "SITE", ["service"] = "UIS",
    ["pointMappings"] = "{\"AveragePumpFillage\":{\"udc\":\"RRLPFILAVG\",\"unit\":\"%\"}}" };
var connection = CygNetAdapter.ReadConnection(settings, out _)!;
var policy = new CygNetReadPolicy();
Check(policy.IsSource(settings, "SITE.UIS::WELL1"), "valid owning-service facility");
foreach (var id in new[] { "OTHER.UIS::WELL1", "SITE.PNT::WELL1", "SITE.UIS::", "SITE.UIS::X::Y", "SITE.UIS::X\n", new string('x', 200) })
    Check(!policy.IsSource(settings, id), "foreign or malformed source refused");
Check(policy.CanRead(settings, "SITE.UIS::WELL1", "AveragePumpFillage", "Current", null, null), "mapped quantity accepted");
Check(!policy.CanRead(settings, "SITE.UIS::WELL1", "PumpFillage", "Current", null, null), "no semantic alias from current to average");
Check(!policy.CanRead(settings, "SITE.UIS::WELL1", "AveragePumpFillage", "History", null, null), "only current reads");
Check(!policy.CanRead(settings, "SITE.UIS::WELL1", "AveragePumpFillage", "Current", "structured.current", "cards"), "structured reads not implied");
foreach (var domain in new[] { "-1", "65536", "abc", "" })
    Check(CygNetAdapter.ReadConnection(settings.ToDictionary(x => x.Key, x => x.Key == "domain" ? domain : x.Value), out _) is null, "invalid domain refused");
foreach (var map in new[] { "{}", "[]", "null", "bad", "{\"Q\":{\"udc\":\"A::B\",\"unit\":\"%\"}}",
    "{\"Q\":{\"udc\":\"A\",\"unit\":\"%\"},\"Q\":{\"udc\":\"B\",\"unit\":\"%\"}}",
    "{\"Q\":{\"udc\":\"A\",\"udc\":\"B\",\"unit\":\"%\"}}", "{\"Q\":{\"udc\":4,\"unit\":\"%\"}}" })
    Check(CygNetAdapter.ReadMappings(settings.ToDictionary(x => x.Key, x => x.Key == "pointMappings" ? map : x.Value)) is null, "invalid mapping refused");
var complete = CygNetAdapter.MapFacilities(connection, [new("WELL1", "Well 1", "RRL")], "service");
Check(complete.Complete && complete.Items.Single().ExternalId == "SITE.UIS::WELL1" && complete.Items[0].Metadata["description"] == "Well 1", "identity and description retained");
Check(!CygNetAdapter.MapFacilities(connection, [new("W"), new("w")], "service").Complete, "duplicates fail closed");
Check(!CygNetAdapter.MapFacilities(connection, [new("W"), new("")], "service").Complete, "invalid identity is incomplete");
var partial = await CygNetAdapter.DiscoverAsync(new FacilityReader(new([new("W")], false)), connection, "service");
Check(!partial.Complete && partial.Items.Count == 1, "partial native discovery stays incomplete");
var truncated = CygNetAdapter.MapFacilities(connection, Enumerable.Range(0, 5001).Select(i => new CygNetFacilitySnapshot("W" + i)).ToArray(), "service");
Check(!truncated.Complete && truncated.Items.Count == 5000, "bounded discovery honestly reports truncation");
var failed = await CygNetAdapter.DiscoverAsync(new FacilityReader(null), connection, "service");
Check(!failed.Complete && !string.Join(' ', failed.Warnings).Contains("SECRET", StringComparison.Ordinal), "vendor failure is redacted");
var acquired = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
Check(CygNetPointEvidence.Create(10m, acquired, 3, 4, 3).Quality == "Good", "neutral initialized/updated bits stay Good");
Check(CygNetPointEvidence.Create(10m, acquired, 7, 4, 3).Quality == "Bad", "unreliable wins over neutral bits");
Check(CygNetPointEvidence.Create(10m, acquired, 8, 4, 3).Quality == "Uncertain", "unknown/configurable bit remains Uncertain");
foreach (var raw in new object?[] { null, "NaN", "Infinity", true, "1e999" })
{
    try { CygNetPointEvidence.Create(raw, acquired, 0, 4, 3); throw new InvalidOperationException("invalid numeric point accepted"); }
    catch (WellSiteAutoPilotException e) when (e.Code == "CYGNET_VALUE_NOT_NUMERIC") { checks++; }
}
try { CygNetPointEvidence.Create(1, DateTime.SpecifyKind(acquired, DateTimeKind.Unspecified), 0, 4, 3); throw new InvalidOperationException("ambiguous time accepted"); }
catch (WellSiteAutoPilotException e) when (e.Code == "CYGNET_TIMESTAMP_INVALID") { checks++; }
var point = new PointReader();
var measurements = new CygNetConnectionMeasurements(point, TimeProvider.System);
var request = new ConnectionMeasurementRequest(Guid.NewGuid(), 1, settings, "SITE.UIS::WELL1", "AveragePumpFillage");
var value = await measurements.ReadAsync(request, default);
Check(value.Value == 42 && value.Unit == "%" && point.Calls == 1 && point.Domain == 123 && point.Udc == "RRLPFILAVG", "saved domain and mapping reach native seam");
await Reject(request with { ConnectionId = Guid.Empty }, "CYGNET_CONNECTION_INVALID");
await Reject(request with { ConnectionRevision = 0 }, "CYGNET_CONNECTION_INVALID");
await Reject(request with { ExternalId = "OTHER.UIS::WELL1" }, "CYGNET_FACILITY_INVALID");
await Reject(request with { Quantity = "PumpFillage" }, "CYGNET_QUANTITY_UNSUPPORTED");
Check(point.Calls == 1, "invalid requests never call native reader");
point.Quality = "Bad";
Check((await measurements.ReadAsync(request, default)).Quality == "Bad", "bad quality never upgraded");
point.Quality = "Uncertain";
Check((await measurements.ReadAsync(request, default)).Quality == "Uncertain", "uncertain remains uncertain");
point.Timestamp = default;
await Reject(request, "CYGNET_TIMESTAMP_INVALID");
var unavailable = new CygNetConnectionMeasurements(new CygNetUnsupportedPointReader(), TimeProvider.System);
try { await unavailable.ReadAsync(request, default); throw new InvalidOperationException("unsupported runtime accepted"); }
catch (WellSiteAutoPilotException e) when (e.Code == "CYGNET_RUNTIME_UNAVAILABLE") { checks++; }

// Force the caller to time out while synchronous work is still blocked. The second call must never start.
var gate = new CygNetCallGate();
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var stop = new CancellationTokenSource();
var first = gate.RunAsync(_ => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); return 1; }, TimeSpan.FromSeconds(5), stop.Token);
await entered.Task;
await stop.CancelAsync();
try { await first; throw new InvalidOperationException("cancellation not observed"); } catch (OperationCanceledException) { checks++; }
var secondStarted = false;
try { await gate.RunAsync(_ => { secondStarted = true; return 2; }, TimeSpan.FromMilliseconds(30), default); }
catch (OperationCanceledException) { checks++; }
Check(!secondStarted, "timed-out native operation keeps slot");
release.SetResult();
Check(await gate.RunAsync(_ => 3, TimeSpan.FromSeconds(5), default) == 3, "slot recovers when actual operation ends");
Console.WriteLine($"CygNet adapter: {checks} checks passed.");

void Check(bool pass, string reason) { if (!pass) throw new InvalidOperationException(reason); checks++; }
async Task Reject(ConnectionMeasurementRequest item, string code)
{
    try { await measurements.ReadAsync(item, default); throw new InvalidOperationException("expected " + code); }
    catch (WellSiteAutoPilotException e) when (e.Code == code) { checks++; }
}
sealed class FacilityReader(CygNetFacilityBatch? batch) : ICygNetFacilityReader
{
    public Task CheckAsync(CygNetConnection c, CancellationToken ct) => Task.CompletedTask;
    public Task<CygNetFacilityBatch> ReadFacilitiesAsync(CygNetConnection c, CancellationToken ct) => batch is null
        ? Task.FromException<CygNetFacilityBatch>(new InvalidOperationException("SECRET")) : Task.FromResult(batch);
}
sealed class PointReader : ICygNetPointReader
{
    public int Calls { get; private set; }
    public ushort Domain { get; private set; }
    public string? Udc { get; private set; }
    public string Quality { get; set; } = "Good";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Task<CygNetPointValue> ReadAsync(CygNetConnection c, string tag, string udc, CancellationToken ct)
    {
        Calls++; Domain = c.Domain; Udc = udc;
        return Task.FromResult(new CygNetPointValue(42, Timestamp, Quality));
    }
}
