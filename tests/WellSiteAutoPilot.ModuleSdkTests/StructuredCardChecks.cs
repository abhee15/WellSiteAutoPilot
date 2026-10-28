using System.Text.Json;
using System.Text.Json.Nodes;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Samples.StructuredCards;

internal static class StructuredCardChecks
{
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static JsonElement Fixture(string name) => Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "structured-cards", name)));

    public static async Task RunAsync()
    {
        var module = new CardCalibrationModule();
        var time = DateTimeOffset.Parse("2026-10-27T12:00:10Z", System.Globalization.CultureInfo.InvariantCulture);
        var asset = Guid.NewGuid();
        var definition = new StructuredModuleDefinition(module.Identity,
            [new(new("cards", "well", "SurfaceCards", "Current", null, 60, false), "sample.cards/v1", Fixture("cards.schema.json")),
             new(new("calibration", "well", "Calibration", "Current", null, null, false), "sample.calibration/v1", Fixture("calibration.schema.json"))],
            [new("calibrated-cards", "CalibratedCards", null, "sample.calibrated-cards/v1", Fixture("calibrated-cards.schema.json")),
             new("peak-load", "PeakLoad", "lbf", "sample.peak-load/v1", Json("""{"type":"number"}"""))]);
        var context = new LogicModuleExecutionContextV2(Guid.NewGuid(), module.Identity, "structured-card-test", time, "{}",
            [new("well", asset, "{}")],
            [new("cards", asset, "SurfaceCards", new("sample.cards/v1", Fixture("cards.json")), null, time.AddSeconds(-10), "Good", "synthetic-fixture"),
             new("calibration", asset, "Calibration", new("sample.calibration/v1", Json("""{"loadScale":2,"loadOffset":10}""")), null,
                 time.AddDays(-1), "Good", "synthetic-configuration")]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        context = JsonSerializer.Deserialize<LogicModuleExecutionContextV2>(JsonSerializer.Serialize(context, options), options)!;
        var result = await StructuredModuleOfflineHost.ExecuteAsync(module, definition, context);
        result = JsonSerializer.Deserialize<LogicModuleExecutionResultV2>(JsonSerializer.Serialize(result, options), options)!;
        var cards = result.EngineeringResults.Single(r => r.Id == "calibrated-cards").Value.Data;
        Assert(cards.GetArrayLength() == 2 && cards[0].GetProperty("load").GetArrayLength() == 5 &&
            cards[1].GetProperty("load").GetArrayLength() == 3, "Ragged card collection was flattened.");
        Assert(cards[0].GetProperty("load")[0].GetDecimal() == 210 &&
            result.EngineeringResults.Single(r => r.Id == "peak-load").Value.Data.GetDecimal() == 490, "Curve/KPI calculation failed.");
        for (var i = 0; i < 2; i++)
        {
            var original = context.Inputs.First().Value.Data[i];
            foreach (var property in new[] { "id", "kind", "representation", "capturedAtUtc", "source", "positionUnit", "loadUnit" })
                Assert(cards[i].GetProperty(property).GetString() == original.GetProperty(property).GetString(), "Card metadata was rewritten.");
            Assert(cards[i].GetProperty("position").GetRawText() == original.GetProperty("position").GetRawText(), "Sample order changed.");
        }
        Assert(cards[1].GetProperty("kind").GetString() == "FULL", "FULL was replaced by REFERENCE.");

        // Host validation happens before user code. Invalid shape, schema, identity,
        // freshness and pairing cannot turn into a successful result or intent.
        var probe = new Probe(module.Identity, result);
        var first = context.Inputs.First();
        LogicModuleExecutionContextV2 Replace(StructuredInputValue input) => context with { Inputs = [input, context.Inputs.Last()] };
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { Value = first.Value with { SchemaId = "other/v1" } })), "MODULE_VALUE_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { Value = first.Value with { Data = Json("42") } })), "MODULE_VALUE_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { TimestampUtc = time.AddSeconds(-61) })), "EXECUTION_INPUT_STALE");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { TimestampUtc = time.AddSeconds(31) })), "EXECUTION_INPUT_FUTURE");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { Quality = "Unknown" })), "EXECUTION_INPUT_QUALITY_REJECTED");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, context with { Inputs = [first, first] }), "MODULE_CONTEXT_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, context with { Inputs = [first] }), "MODULE_CONTEXT_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, Replace(first with { AssetId = Guid.NewGuid() })), "MODULE_CONTEXT_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition, context with { ParametersJson = new string('x', 262144) }), "MODULE_ENVELOPE_TOO_LARGE");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe,
            definition with { Inputs = [definition.Inputs.First() with { Requirement = definition.Inputs.First().Requirement with { MaximumAgeSeconds = -1 } }] },
            context), "MODULE_CONTEXT_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition,
            Replace(first with { Value = first.Value with { Data = default } })), "MODULE_ENVELOPE_INVALID");
        Assert(probe.Calls == 0, "Input rejection ran module code.");
        var mismatched = JsonNode.Parse(first.Value.Data.GetRawText())!;
        mismatched[0]!["position"]!.AsArray().RemoveAt(0);
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(module, definition,
            Replace(first with { Value = first.Value with { Data = Json(mismatched.ToJsonString()) } })), "CARD_SAMPLES_INVALID");
        var unknownUnit = JsonNode.Parse(first.Value.Data.GetRawText())!;
        unknownUnit[0]!["loadUnit"] = "kg";
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(probe, definition,
            Replace(first with { Value = first.Value with { Data = Json(unknownUnit.ToJsonString()) } })), "MODULE_VALUE_INVALID");

        var invalidResult = result with { EngineeringResults = [result.EngineeringResults.Last() with { Value = new("sample.peak-load/v1", Json("true")) }] };
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(new Probe(module.Identity, invalidResult), definition, context), "MODULE_VALUE_INVALID");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(new Probe(module.Identity, result with { FailureCode = "CALCULATION_FAILED" }), definition, context), "CALCULATION_FAILED");
        await Reject(() => StructuredModuleOfflineHost.ExecuteAsync(new Probe(module.Identity, result with { FailureCode = "bad code" }), definition, context), "MODULE_RESULT_INVALID");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await StructuredModuleOfflineHost.ExecuteAsync(probe, definition, context, cancelled.Token);
            throw new InvalidOperationException("Cancellation ignored.");
        }
        catch (OperationCanceledException) { }

        // The framework also admits non-RRL scalar values, vectors, matrices and records.
        foreach (var (data, schema) in new[]
        {
            ("true", """{"type":"boolean"}"""),
            ("\"status\"", """{"type":"string"}"""),
            ("1.2345678901234567890123456789", """{"type":"number"}"""),
            ("42", """{"type":"integer"}"""),
            ("null", """{"type":"null"}"""),
            ("[1,2]", """{"type":"array","items":{"type":"number"}}"""),
            ("[[1,2],[3,4]]", """{"type":"array","items":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":2}}"""),
            ("""{"name":"curve","active":true}""", """{"type":"object","properties":{"name":{"type":"string"},"active":{"type":"boolean"}},"required":["name","active"],"additionalProperties":false}""")
        })
        {
            var value = new StructuredValue("test/v1", Json(data));
            await StructuredValueValidation.ValidateAsync(value, "test/v1", Json(schema));
            Assert(value.Data.GetRawText() == data, "Validation coerced the JSON value.");
        }
        foreach (var (data, schema) in new[]
        {
            ("1e999", """{"type":"number"}"""),
            ("\"NaN\"", """{"type":"number"}"""),
            ("""{"x":1,"x":2}""", """{"type":"object"}"""),
            ("[1]", """{"type":"array","items":{"$ref":"https://example.invalid/schema"}}"""),
            ("[1]", """{"type":"array","type":"object"}"""),
            ("[[1],[2,3]]", """{"type":"array","items":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":2}}"""),
            ("\"" + new string('x', 65536) + "\"", """{"type":"string"}"""),
            ("1", """{"type":"number","unknownKeyword":true}""")
        })
            await Reject(() => StructuredValueValidation.ValidateAsync(new("test/v1", Json(data)), "test/v1", Json(schema)), "MODULE_VALUE_INVALID");
        Console.WriteLine("Structured SDK/card collection checks passed (offline only).");
    }

    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class Probe(LogicModuleIdentity identity, LogicModuleExecutionResultV2 result) : ILogicModuleV2
    {
        public int Calls { get; private set; }
        public LogicModuleIdentity Identity { get; } = identity;
        public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(result); }
    }
}
