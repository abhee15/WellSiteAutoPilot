using System.Text.Json;
using System.Text.Json.Nodes;

namespace WellSiteAutoPilot.Application.Authoring;

public sealed record AuthoringStarter(string Id, string Label, string Description, AuthoringDocument Document);

/// <summary>Reviewed starting drafts. Each is an ordinary draft; nothing here is executable until built.</summary>
public static class AuthoringStarters
{
    public const string CardCalibrationId = "card-calibration";
    public const string ThresholdAlertId = "threshold-alert";
    public const string ObserveInputId = "observe-input";

    public static IReadOnlyList<AuthoringStarter> All { get; } =
    [
        new(ThresholdAlertId, "Threshold alert",
            "Pump fillage against a threshold: shortfall and ratio calculations, a condition and a proposed adjustment.",
            ThresholdAlert()),
        new(CardCalibrationId, "Card calibration (structured, code-backed)",
            "Reproduces the structured card calibration sample: card collection plus calibration record, calibrated curves and peak load. The calibration algorithm is an opaque code step.",
            CardCalibration()),
        new(ObserveInputId, "Observe one input",
            "Pass one scalar input through as an output.", ObserveInput())
    ];

    public static AuthoringStarter? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    private static AuthoringDocument ObserveInput() => new(
        AuthoringDocument.SchemaId,
        new("authored.observe-input", "1.0.0", "Observe one input", "Authoring",
            "Observe one supplied pump-fillage input. No provider writes."),
        AuthoringDocument.DecimalProfile,
        [new("well", "well")],
        [new("pump-fillage", "Pump fillage", "PumpFillage", "%", "well", 60)],
        [], [], [],
        [new("observed-value", "Observed value", "PumpFillage", "%", new("input", "pump-fillage"))],
        []);

    private static AuthoringDocument ThresholdAlert() => new(
        AuthoringDocument.SchemaId,
        new("authored.fillage-threshold-alert", "1.0.0", "Pump fillage threshold alert", "Authoring",
            "Proposes a review when pump fillage falls below a configurable threshold. Advisory only; no provider writes."),
        AuthoringDocument.DecimalProfile,
        [new("well", "well")],
        [new("pump-fillage", "Pump fillage", "PumpFillage", "%", "well", 60)],
        [new("low-threshold", "Low fillage threshold", "40", "%")],
        [
            new("shortfall", "Shortfall below threshold", "calculation", "subtract",
                [new("parameter", "low-threshold"), new("input", "pump-fillage")]),
            new("fillage-ratio", "Fillage as a fraction of threshold", "calculation", "divide",
                [new("input", "pump-fillage"), new("parameter", "low-threshold")])
        ],
        [new("is-low", "Fillage below threshold", new("input", "pump-fillage"), "lt", new("parameter", "low-threshold"))],
        [
            new("observed-fillage", "Observed fillage", "PumpFillage", "%", new("input", "pump-fillage")),
            new("fillage-shortfall", "Shortfall", "FillageShortfall", "%", new("step", "shortfall")),
            new("fillage-ratio-result", "Ratio to threshold", "FillageRatio", "1", new("step", "fillage-ratio"))
        ],
        [new("review-low-fillage", "Review low fillage", "is-low", "Pump fillage below threshold",
            "Observed pump fillage is below the configured threshold. Review the well; this is a proposal only.",
            "PumpFillageThreshold", "%", new("parameter", "low-threshold"))],
        [
            new("low-fillage", "Fillage 30% (below threshold)",
                new Dictionary<string, JsonElement> { ["pump-fillage"] = Parse("30") }, null,
                new Dictionary<string, JsonElement>
                {
                    ["observed-fillage"] = Parse("30"), ["fillage-shortfall"] = Parse("10"), ["fillage-ratio-result"] = Parse("0.75")
                }, ["review-low-fillage"]),
            new("healthy-fillage", "Fillage 55% (above threshold)",
                new Dictionary<string, JsonElement> { ["pump-fillage"] = Parse("55") }, null,
                new Dictionary<string, JsonElement> { ["fillage-shortfall"] = Parse("-15"), ["fillage-ratio-result"] = Parse("1.375") }, [])
        ]);

    private static AuthoringDocument CardCalibration() => new(
        AuthoringDocument.SchemaId,
        new(CardModuleId, CardModuleVersion, "Synthetic card calibration", "Authoring",
            "Calibrate surface cards with a linear load scale and offset and report the peak load."),
        AuthoringDocument.DecimalProfile,
        [new("well", null)],
        [
            new("cards", "Surface cards", "SurfaceCards", null, "well", 60, false,
                new("sample.cards/v1", CardSchema(calibrated: false)), "structured.current"),
            new("calibration", "Calibration", "Calibration", null, "well", null, false,
                new("sample.calibration/v1", CalibrationSchema()), "structured.current")
        ],
        [],
        [
            new("calibrate", "Linear load calibration and peak", "code", null, null,
                new("csharp", "WellSiteAutoPilot.Samples.StructuredCards.CardCalibrationModule", CardSource,
                    ["cards", "calibration"], ["calibrated-cards", "peak-load"]))
        ],
        [],
        [
            new("calibrated-cards", "Calibrated cards", "CalibratedCards", "", new("step", "calibrate", "calibrated-cards"),
                new("sample.calibrated-cards/v1", CardSchema(calibrated: true))),
            new("peak-load", "Peak load", "PeakLoad", "lbf", new("step", "calibrate", "peak-load"),
                new("sample.peak-load/v1", Parse("""{"type":"number"}""")))
        ],
        [],
        [
            new("one-card", "One card, scale 1.1 offset 50",
                new Dictionary<string, JsonElement>
                {
                    ["cards"] = Parse("""
                        [{"id":"card-1","kind":"CURRENT","representation":"SURFACE","capturedAtUtc":"2025-12-31T23:00:00Z",
                        "source":"fixture","positionUnit":"in","loadUnit":"lbf","position":[0,10,20],"load":[1000,2000,1500]}]
                        """),
                    ["calibration"] = Parse("""{"loadScale":1.1,"loadOffset":50}""")
                }, null,
                new Dictionary<string, JsonElement> { ["peak-load"] = Parse("2250") }, [])
        ]);

    public const string CardModuleId = "authored.card-calibration";
    public const string CardModuleVersion = "1.0.0";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement CalibrationSchema() => Parse("""
        {"type":"object","properties":{"loadScale":{"type":"number","minimum":0.1,"maximum":10},
        "loadOffset":{"type":"number","minimum":-10000,"maximum":10000}},
        "required":["loadScale","loadOffset"],"additionalProperties":false}
        """);

    private static JsonElement CardSchema(bool calibrated)
    {
        JsonObject Text(params string[] values)
        {
            var node = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 };
            if (values.Length > 0) node["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
            return node;
        }
        JsonObject Samples() => new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "number", ["minimum"] = -1000000, ["maximum"] = 1000000 },
            ["minItems"] = 2, ["maxItems"] = 1024
        };
        var properties = new JsonObject
        {
            ["id"] = Text(), ["kind"] = Text("CURRENT", "PUMPOFF", "FULL", "REFERENCE"), ["representation"] = Text("SURFACE"),
            ["capturedAtUtc"] = Text(), ["source"] = Text(), ["positionUnit"] = Text("in"), ["loadUnit"] = Text("lbf"),
            ["position"] = Samples(), ["load"] = Samples()
        };
        var required = new List<string> { "id", "kind", "representation", "capturedAtUtc", "source", "positionUnit", "loadUnit", "position", "load" };
        if (calibrated) { properties["calculationMethod"] = Text("LinearLoadCalibration"); required.Add("calculationMethod"); }
        var schema = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "object", ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()), ["additionalProperties"] = false
            },
            ["minItems"] = 1, ["maxItems"] = 16
        };
        return JsonDocument.Parse(schema.ToJsonString()).RootElement.Clone();
    }

    /// <summary>The structured card sample, verbatim except for the module identity and a note.</summary>
    public const string CardSource = """
using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Samples.StructuredCards;

// Synthetic calibration example, NOT Gibbs/Everitt-Jennings downhole analysis.
// It proves that a module can consume card collections plus a configuration record
// and return both curves and a scalar. No provider or customer UDC knowledge here.
public sealed class CardCalibrationModule : ILogicModuleV2
{
    public LogicModuleIdentity Identity { get; } = new("authored.card-calibration", "1.0.0");

    public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context,
        CancellationToken cancellationToken = default)
    {
        var cards = context.Inputs.Single(i => i.RequirementId == "cards");
        var configuration = context.Inputs.Single(i => i.RequirementId == "calibration").Value.Data;
        var scale = configuration.GetProperty("loadScale").GetDecimal();
        var offset = configuration.GetProperty("loadOffset").GetDecimal();
        var calibrated = new List<object>();
        decimal? peakLoad = null;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards.Value.Data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = card.GetProperty("position").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
            var load = card.GetProperty("load").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
            var captureText = card.GetProperty("capturedAtUtc").GetString()!;
            if (position.Length != load.Length || !identities.Add(card.GetProperty("id").GetString()!) ||
                !captureText.EndsWith('Z') ||
                !DateTimeOffset.TryParse(captureText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var capturedAt) ||
                capturedAt > context.EvaluationTimeUtc.AddSeconds(30))
                return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_REJECTED", [], [], [],
                    new Dictionary<string, string>()) { FailureCode = "CARD_SAMPLES_INVALID" });
            var adjusted = load.Select(v => checked(v * scale + offset)).ToArray();
            var peak = adjusted.Max();
            peakLoad = peakLoad is null ? peak : Math.Max(peakLoad.Value, peak);
            calibrated.Add(new
            {
                id = card.GetProperty("id").GetString(), kind = card.GetProperty("kind").GetString(),
                representation = card.GetProperty("representation").GetString(),
                capturedAtUtc = captureText, source = card.GetProperty("source").GetString(),
                positionUnit = card.GetProperty("positionUnit").GetString(),
                loadUnit = card.GetProperty("loadUnit").GetString(), position, load = adjusted,
                calculationMethod = "LinearLoadCalibration"
            });
        }
        return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_CALIBRATION_COMPLETED",
            [new("calibrated-cards", cards.AssetId, "CalibratedCards",
                new("sample.calibrated-cards/v1", JsonSerializer.SerializeToElement(calibrated)), null),
             new("peak-load", cards.AssetId, "PeakLoad",
                new("sample.peak-load/v1", JsonSerializer.SerializeToElement(peakLoad)), "lbf")],
            [], [], new Dictionary<string, string>()));
    }
}

""";
}
