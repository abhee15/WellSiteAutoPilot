using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Logic.Rules;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class RulesModuleChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Definition = """
        {
          "parameters": [
            { "id": "LowFillage", "unit": "%", "default": 70, "minimum": 0, "maximum": 100, "assignable": true },
            { "id": "SpeedStep", "unit": "spm", "default": 0.5, "minimum": 0.1, "maximum": 2, "assignable": true }
          ],
          "calculations": [
            { "id": "Shortfall", "expression": "LowFillage - PumpFillage", "unit": "%" }
          ],
          "rules": [
            {
              "id": "SlowDown",
              "when": "Shortfall > 0",
              "reason": "Fillage is below the limit, so slow the pump to let it fill.",
              "outcomeCode": "SLOW_PUMP",
              "title": "Reduce pump speed",
              "recommend": {
                "setpoint": "PumpSpeed",
                "valueExpression": "CurrentSpeed - SpeedStep",
                "unit": "spm",
                "currentValue": "CurrentSpeed",
                "minimum": 2,
                "maximum": 12,
                "maximumChange": 1
              }
            }
          ]
        }
        """;

    public static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var definition = RulesDefinition.Parse(Definition);
        var manifest = new LogicModuleManifest(1, "guided.target-fillage", "1.0.0", "Target fillage", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, RulesDefinition.EntryPoint, "1.1.0",
            [new("well", 1, 1, null, [])],
            [new("fillage", "well", "PumpFillage", "Current", "%", 3600, false),
             new("speed", "well", "CurrentSpeed", "Current", "spm", 3600, false)], [],
            ParameterSchema: JsonSerializer.Deserialize<JsonElement>(DeclarativeRulesModule.ParameterSchemaJson(definition)));

        using var bytes = new MemoryStream(Pack(manifest, Definition));
        var inspected = await LogicModulePackageReader.ReadAsync(bytes);
        var module = inspected.RulesModule ?? throw new InvalidOperationException("A rules package must load a rules module.");
        if (inspected.DeclarativeModule is not null)
            throw new InvalidOperationException("A rules package is not a threshold module.");

        var asset = Guid.NewGuid();
        var low = await module.ExecuteAsync(Context(module, asset, now, 55m, 6m, "{}", "{}"));
        if (low.OutcomeCode != "SLOW_PUMP" || low.Recommendations.Count != 1 ||
            low.Recommendations.Single() is not { Quantity: "PumpSpeed", SuggestedValue: 5.5m, Unit: "spm", AssetId: var target } ||
            target != asset)
            throw new InvalidOperationException("The low fillage case must recommend a slower pump speed.");
        if (low.EngineeringResults.Single() is not { Id: "Shortfall", Value: 15m, Unit: "%" })
            throw new InvalidOperationException("The calculation must be reported with its unit.");

        var overridden = await module.ExecuteAsync(Context(module, asset, now, 55m, 6m,
            """{"SpeedStep":2}""", """{"SpeedStep":1.5}"""));
        if (overridden.Recommendations.Single().SuggestedValue != 5m)
            throw new InvalidOperationException("The asset override must win and the change must be held to its limit.");

        var fine = await module.ExecuteAsync(Context(module, asset, now, 90m, 6m, "{}", "{}"));
        if (fine.OutcomeCode != RulesDefinition.NoMatchOutcome || fine.Recommendations.Count != 0)
            throw new InvalidOperationException("No rule matching must produce no recommendation.");

        await RejectAsync(() => module.ExecuteAsync(Context(module, asset, now, 55m, 6m, """{"LowFillage":150}""", "{}")).AsTask(),
            "CONFIGURED_LOGIC_PARAMETERS_INVALID");
        await RejectAsync(() => module.ExecuteAsync(Context(module, asset, now, 55m, 6m, "{}", "{}", now.AddHours(3))).AsTask(),
            "EXECUTION_INPUT_STALE");

        var broken = Definition.Replace("Shortfall > 0", "Shortfall + 1");
        await RejectAsync(() => LogicModulePackageReader.ReadAsync(new MemoryStream(Pack(manifest, broken))),
            "MODULE_DECLARATIVE_CONTRACT_INVALID");
    }

    private static LogicModuleExecutionContext Context(DeclarativeRulesModule module, Guid asset, DateTimeOffset now,
        decimal fillage, decimal speed, string settings, string overrides, DateTimeOffset? evaluation = null) =>
        new(Guid.NewGuid(), module.Identity, "corr", evaluation ?? now, settings, [new("well", asset, overrides)],
        [
            new("fillage", asset, "PumpFillage", fillage, "%", now.AddMinutes(-5), "Good", "fixture"),
            new("speed", asset, "CurrentSpeed", speed, "spm", now.AddMinutes(-5), "Good", "fixture")
        ]);

    private static byte[] Pack(LogicModuleManifest manifest, string definition)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write(JsonSerializer.Serialize(manifest, Options));
            using (var writer = new StreamWriter(archive.CreateEntry(DeclarativeRulesModule.PayloadPath).Open()))
                writer.Write(definition);
        }

        return bytes.ToArray();
    }

    private static async Task RejectAsync(Func<Task> operation, string code)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }
}
