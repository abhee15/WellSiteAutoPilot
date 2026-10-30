using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Samples.StructuredCards;

// Disposable coordinator, NOT a workflow engine, authoring API or authorization policy.
var checks = new List<object>();
void Check(string name, bool passed)
{
    checks.Add(new { name, passed });
    if (!passed) throw new InvalidOperationException(name);
}
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;
JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
var module = new CardCalibrationModule();
using var stream = File.OpenRead(Path.Combine(root, "manifest.json"));
var definition = await StructuredModuleOfflineHost.ReadDefinitionAsync(stream);
var time = DateTimeOffset.Parse("2026-10-27T12:00:10Z", System.Globalization.CultureInfo.InvariantCulture);
var asset = Guid.Parse("00000000-0000-4000-8000-000000000001");
var context = new LogicModuleExecutionContextV2(Guid.NewGuid(), module.Identity, "composition-probe", time, "{}",
    [new("well", asset, "{}")],
    [new("cards", asset, "SurfaceCards", new("sample.cards/v1", Json(File.ReadAllText(Path.Combine(root, "cards.json")))),
        null, time.AddSeconds(-10), "Good", "synthetic-foresite-A"),
     new("calibration", asset, "Calibration", new("sample.calibration/v1", Json("""{"loadScale":2,"loadOffset":10}""")),
        null, time.AddDays(-1), "Good", "synthetic-foresite-B")]);
context = RoundTrip(context);
var result = RoundTrip(await StructuredModuleOfflineHost.ExecuteAsync(module, definition, context));
var peak = result.EngineeringResults.Single(r => r.Id == "peak-load").Value.Data.GetDecimal();
var curves = result.EngineeringResults.Single(r => r.Id == "calibrated-cards").Value.Data;
Check("Real sample: dependent calibration produces peak 490", peak == 490);
Check("Real sample: ragged collections and typed scalar survive JSON", curves[0].GetProperty("load").GetArrayLength() == 5 &&
    curves[1].GetProperty("load").GetArrayLength() == 3 && result.EngineeringResults.Last().Value.Data.ValueKind == JsonValueKind.Number);
Check("Real sample: source capture metadata remains attached", curves[1].GetProperty("capturedAtUtc").GetString() ==
    context.Inputs.First().Value.Data[1].GetProperty("capturedAtUtc").GetString());

var altered = context with { Inputs = [context.Inputs.First(), context.Inputs.Last() with
    { Value = new("sample.calibration/v1", Json("""{"loadScale":1,"loadOffset":10}""")) }] };
var changed = await StructuredModuleOfflineHost.ExecuteAsync(module, definition, altered);
Check("Upstream parameter edit changes actual sample output", changed.EngineeringResults.Last().Value.Data.GetDecimal() == 250);
Check("Context export/import retains semantic inputs", JsonSerializer.Serialize(altered, options) == JsonSerializer.Serialize(RoundTrip(altered), options));

var broken = JsonNode.Parse(context.Inputs.First().Value.Data.GetRawText())!;
broken[0]!["position"]!.AsArray().RemoveAt(0);
try
{
    await StructuredModuleOfflineHost.ExecuteAsync(module, definition, context with { Inputs =
        [context.Inputs.First() with { Value = new("sample.cards/v1", Json(broken.ToJsonString())) }, context.Inputs.Last()] });
    Check("Paired-array mismatch rejects the real sample", false);
}
catch (WellSiteAutoPilotException error) { Check("Paired-array mismatch rejects the real sample", error.Code == "CARD_SAMPLES_INVALID"); }

// Named bindings below are a PROPOSED authoring envelope, not current manifest fields.
// Same external ID deliberately names two distinct configured sources.
var bindings = RoundTrip(new[] {
    new Binding("cards", "foresite", "source-A", 3, asset, "597", "sample.cards/v1", "fixture"),
    new Binding("calibration", "foresite", "source-B", 8, asset, "597", "sample.calibration/v1", "fixture"),
    new Binding("analysis", "wami-stub", "calculation-C", 2, asset, null, "probe.predicted-load/v1", "synthetic-model-v1")
});
Check("Named dependency round trip preserves distinct sources with identical external IDs",
    bindings.Length == 3 && bindings[0].ExternalId == bindings[1].ExternalId && bindings[0].Connection != bindings[1].Connection);
Check("Proposed envelope pins individual connection/model versions", bindings[1].Revision == 8 && bindings[2].ModelVersion == "synthetic-model-v1");
// This coordinator consumes each named binding; its policy and service are synthetic.
var model = System.Text.Encoding.UTF8.GetBytes("{\"factor\":0.8}");
var modelHash = Convert.ToHexStringLower(SHA256.HashData(model));
var calls = 0;
async Task<decimal> Compose(Binding[] selected, Func<Binding, bool> allowed, byte[] modelBytes, CancellationToken token)
{
    if (selected.Length != bindings.Length || selected.Select(b => b.Role).Distinct().Count() != bindings.Length)
        throw new InvalidOperationException("DEPENDENCY_SET_INVALID");
    foreach (var expected in bindings)
    {
        var chosen = selected.Single(b => b.Role == expected.Role);
        if (chosen != expected) throw new InvalidOperationException("DEPENDENCY_PIN_MISMATCH");
        if (!allowed(chosen)) throw new InvalidOperationException("SYNTHETIC_POLICY_DENIED");
    }
    if (modelBytes.Length > 1024 || Convert.ToHexStringLower(SHA256.HashData(modelBytes)) != modelHash)
        throw new InvalidOperationException("MODEL_ARTIFACT_MISMATCH");
    var calculation = selected.Single(b => b.Role == "analysis");
    if (calculation.Adapter != "wami-stub" || calculation.Schema != "probe.predicted-load/v1")
        throw new InvalidOperationException("CALCULATION_UNSUPPORTED");
    token.ThrowIfCancellationRequested();
    calls++;
    await Task.Delay(30, token); // Makes in-flight cancellation observable, not merely pre-cancellation.
    using var parsed = JsonDocument.Parse(modelBytes);
    return peak * parsed.RootElement.GetProperty("factor").GetDecimal();
}
async Task Reject(string label, Binding[] selected, Func<Binding, bool> allowed, byte[] bytes, string code)
{
    var before = calls;
    try { await Compose(selected, allowed, bytes, CancellationToken.None); Check(label, false); }
    catch (InvalidOperationException error) { Check(label, error.Message == code && before == calls); }
}
var predicted = await Compose(bindings, _ => true, model, CancellationToken.None);
var margin = predicted - 300m;
Check("Bound coordinator invokes a separately configured stub using pinned model bytes", predicted == 392m && margin == 92m);
foreach (var (label, bad) in new[] {
    ("connection revision", bindings[2] with { Revision = 99 }),
    ("connection identity", bindings[2] with { Connection = "other" }),
    ("logical asset", bindings[2] with { LogicalAsset = Guid.NewGuid() }),
    ("schema", bindings[2] with { Schema = "unknown/v1" }),
    ("model version", bindings[2] with { ModelVersion = "wrong" }) })
    await Reject("Probe coordinator rejects changed " + label, [bindings[0], bindings[1], bad], _ => true, model, "DEPENDENCY_PIN_MISMATCH");
await Reject("Denying one data dependency prevents stub dispatch (synthetic policy)", bindings,
    b => b.Role != "calibration", model, "SYNTHETIC_POLICY_DENIED");
await Reject("Changed model bytes prevent dispatch", bindings, _ => true, [1, 2, 3], "MODEL_ARTIFACT_MISMATCH");
using var cancelled = new CancellationTokenSource();
var callsBeforeCancel = calls;
var pending = Compose(bindings, _ => true, model, cancelled.Token);
cancelled.Cancel();
try { await pending; Check("In-flight stub cancellation", false); }
catch (OperationCanceledException) { Check("In-flight stub cancellation", calls == callsBeforeCancel + 1); }
Check("Sample emitted no production intents or state changes", result.ControlIntents.Count == 0 && result.Recommendations.Count == 0 && result.StateUpdates.Count == 0);
var proposed = new ControlIntentDraft("PROBE_REVIEW", asset, "SyntheticAdjustment", "SyntheticTarget", margin, "lbf", "PROBE_ONLY");
var localReport = RoundTrip(new { proposedActions = new[] { proposed }, stateUpdates = new Dictionary<string, string> { ["probe-state"] = "captured-only" } });
Check("Proposed actions and state round trip as test-report records", localReport.proposedActions.Single().RequestedValue == 92m && localReport.stateUpdates["probe-state"] == "captured-only");

var report = new {
    probe = "complex-sample-and-composition", status = "passed", checks,
    observations = new { peak, predicted, margin, bindings, modelHash,
        trace = new[] { "cards + calibration -> calibrated curves", "curves -> peak 490", "peak -> WAMI-shaped stub 392", "predicted - threshold 300 -> margin 92" } },
    notEstablished = new[] {
        "Live ForeSite or WAMI access; all provider labels are synthetic fixtures",
        "The binding coordinator and policy above are disposable references, not product routing or authority",
        "Production command denial, external side-effect detection and ambiguous calculation recovery",
        "Current runtime multi-provider orchestration or per-dependency authorization",
        "Graph-to-code compilation or lossless arbitrary source-code import into a visual editor",
        "SDK intermediate-variable instrumentation; trace above is explicitly authored by this probe",
        "Build/test isolation, hosted preview, authenticated artifact handoff or physical control" }
};
var output = args.Length == 1 ? args[0] : throw new ArgumentException("Report output path required");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, options) + Environment.NewLine);
Console.WriteLine($"{checks.Count} checks passed; report: {output}");

internal sealed record Binding(string Role, string Adapter, string Connection, int Revision,
    Guid LogicalAsset, string? ExternalId, string Schema, string ModelVersion);
