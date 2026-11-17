using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

internal static class StructuredTraceTransportChecks
{
    public static void Run()
    {
        var asset = Guid.NewGuid();
        var result = new LogicModuleExecutionResultV2("CARD_CALIBRATION_COMPLETED",
            [new("peak-load", asset, "PeakLoad",
                new("sample.peak-load/v2", JsonSerializer.SerializeToElement(490m)), "lb")],
            [], [], new Dictionary<string, string>());
        var legacy = new LogicModuleExecutionResult(result.OutcomeCode, [], [], [],
            new Dictionary<string, string>());
        var observed = new ObservedExecutionResult(legacy, [])
        { StructuredResult = result, StructuredInputs = [] };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Preserve canonical JSON byte-for-byte for older V2 results without a trace.
        var legacyStructuredJson = JsonSerializer.Serialize(new
        {
            result.OutcomeCode, result.EngineeringResults, result.Recommendations,
            result.ControlIntents, result.StateUpdates, InputObservations = observed.StructuredInputs,
            ValueContractVersion = 2
        }, options);
        var uninstrumentedJson = observed.SerializeOutput();
        Assert(uninstrumentedJson == legacyStructuredJson,
            "Uninstrumented structured execution output changed shape or property ordering.");
        using (var doc = JsonDocument.Parse(uninstrumentedJson))
        {
            Assert(!doc.RootElement.TryGetProperty("calculationTrace", out _),
                "Uninstrumented module gained a misleading calculation trace.");
        }

        var trace = new StructuredCalculationTraceV1(1,
        [
            new("calibrate-card-1", "Transform", ["cards", "calibration"], [],
                [new("peakLoad", 410m, "lb")]),
            new("aggregate-peak-load", "Aggregate", [], ["calibrate-card-1"],
                [new("peakLoad", 490m, "lb")])
        ]);
        var instrumentedJson = (observed with
        {
            StructuredResult = result with { CalculationTrace = trace }
        }).SerializeOutput();

        using var instrumented = JsonDocument.Parse(instrumentedJson);
        var root = instrumented.RootElement;
        Assert(root.GetProperty("outcomeCode").GetString() == "CARD_CALIBRATION_COMPLETED" &&
            root.GetProperty("valueContractVersion").GetInt32() == 2 &&
            root.GetProperty("engineeringResults")[0].GetProperty("value").GetProperty("data").GetDecimal() == 490m,
            "Instrumented execution changed final engineering outputs.");
        var traceJson = root.GetProperty("calculationTrace");
        var steps = traceJson.GetProperty("steps");
        Assert(traceJson.GetProperty("version").GetInt32() == 1 && steps.GetArrayLength() == 2 &&
            steps[0].GetProperty("id").GetString() == "calibrate-card-1" &&
            steps[1].GetProperty("priorStepIds")[0].GetString() == "calibrate-card-1" &&
            steps[1].GetProperty("metrics")[0].GetProperty("value").GetDecimal() == 490m,
            "Structured module trace was not serialized as explicit versioned evidence.");

        var roundTrip = JsonSerializer.Deserialize<StructuredCalculationTraceV1>(traceJson.GetRawText(), options);
        Assert(roundTrip?.Version == 1 && roundTrip.Steps.Count == 2,
            "The versioned trace does not round-trip through the worker's JSON contract.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
