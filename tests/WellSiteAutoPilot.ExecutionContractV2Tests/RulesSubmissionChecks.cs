using WellSiteAutoPilot.Application.Logic.Rules;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class RulesSubmissionChecks
{
    private const string Rules = """
        {
          "parameters": [ { "id": "LowFillage", "unit": "%", "default": 70, "minimum": 0, "maximum": 100, "assignable": true } ],
          "calculations": [ { "id": "Shortfall", "expression": "LowFillage - PumpFillage", "unit": "%" } ],
          "rules": [ { "id": "Low", "when": "Shortfall > 0", "reason": "Fillage is below the limit.",
                       "outcomeCode": "LOW_FILLAGE", "title": null, "recommend": null } ]
        }
        """;

    public static async Task RunAsync()
    {
        var manifest = new LogicModuleManifest(1, "guided.rules-submit", "1.0.0", "Rules submit", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, "contract.unset", "1.1.0",
            [new("well", 1, 1, null, [])],
            [new("fillage", "well", "PumpFillage", "Current", "%", 3600, false)], []);
        var definition = RulesDefinition.Parse(Rules);

        var first = await RulesPackageBuilder.BuildAsync(manifest, definition);
        var second = await RulesPackageBuilder.BuildAsync(manifest, definition);
        if (first.Package.Sha256 != second.Package.Sha256 || first.Package.RulesModule is null)
            throw new InvalidOperationException("Packaging must be deterministic and produce a rules module.");

        var cases = new RulesCase[]
        {
            new("Low", new Dictionary<string, decimal> { ["PumpFillage"] = 50 }, new Dictionary<string, decimal>(), "LOW_FILLAGE"),
            new("Fine", new Dictionary<string, decimal> { ["PumpFillage"] = 90 }, new Dictionary<string, decimal>(), "NONE")
        };
        var evidence = GuidedRulesCases.Run(definition, ["PumpFillage"], cases);
        if (evidence.Count != 2 || evidence[0].OutcomeCode != "LOW_FILLAGE")
            throw new InvalidOperationException("Scenarios must record what ran.");

        await Reject(() => GuidedRulesCases.Run(definition, ["PumpFillage"],
            [cases[0] with { ExpectedOutcome = "NONE" }]), "GUIDED_RULES_EXPECTATION_FAILED");
        await Reject(() => GuidedRulesCases.Run(definition, ["PumpFillage"], []), "GUIDED_RULES_CASES_INVALID");
        await Reject(() => GuidedRulesCases.Run(definition, ["PumpFillage"], [cases[0], cases[0]]), "GUIDED_RULES_CASES_INVALID");

        // A rule that names a command is declared by the package, so the gate can see what the module may ask for.
        var commanded = RulesDefinition.Parse(Rules.Replace("\"recommend\": null",
            "\"recommend\": { \"setpoint\": \"speed\", \"valueExpression\": \"5\", \"unit\": \"spm\", \"currentValue\": null, \"minimum\": null, \"maximum\": null, \"maximumChange\": null, \"command\": \"SET_SPEED\" }"));
        var commandArtifact = await RulesPackageBuilder.BuildAsync(manifest, commanded);
        if (commandArtifact.Package.Manifest.CommandRequirements.Single() is not { Command: "SET_SPEED", Quantity: "speed", CanonicalUnit: "spm" })
            throw new InvalidOperationException("A rule's command must be declared in the package manifest.");
        var conflict = RulesDefinition.Parse(Rules.Replace("\"recommend\": null",
            "\"recommend\": { \"setpoint\": \"speed\", \"valueExpression\": \"5\", \"unit\": \"spm\", \"currentValue\": null, \"minimum\": null, \"maximum\": null, \"maximumChange\": null, \"command\": \"SET_SPEED\" }")
            .Replace("\"rules\": [", "\"rules\": [ { \"id\": \"Other\", \"when\": \"PumpFillage > 99\", \"reason\": \"r\", \"outcomeCode\": \"OTHER\", \"title\": null, \"recommend\": { \"setpoint\": \"speed\", \"valueExpression\": \"4\", \"unit\": \"rpm\", \"currentValue\": null, \"minimum\": null, \"maximum\": null, \"maximumChange\": null, \"command\": \"SET_SPEED\" } },"));
        await Reject(() => RulesPackageBuilder.BuildAsync(manifest, conflict), "GUIDED_RULES_COMMAND_CONFLICT");
        var daily = manifest with { DataRequirements = [new("fillage", "well", "PumpFillage", "Daily", "%", 3600, false)] };
        await Reject(() => RulesPackageBuilder.BuildAsync(daily, definition), "GUIDED_RULES_INPUT_UNSUPPORTED");
        var unknownCapability = manifest with
        {
            DataRequirements = [new("fillage", "well", "PumpFillage", "Current", "%", 3600, false, null, "structured.current")]
        };
        await Reject(() => RulesPackageBuilder.BuildAsync(unknownCapability, definition), "GUIDED_RULES_INPUT_UNSUPPORTED");

        // A production-day value runs in a rules module; the reviewed package keeps its capability so it is read as daily.
        var productionDay = manifest with
        {
            DataRequirements = [new("fillage", "well", "PumpFillage", "Current", "%", 3600, false, null, "engineering.daily")]
        };
        var dailyArtifact = await RulesPackageBuilder.BuildAsync(productionDay, definition);
        if (dailyArtifact.Package.Manifest.DataRequirements.Single().RequiredCapability != "engineering.daily" ||
            dailyArtifact.Package.RulesModule is null)
            throw new InvalidOperationException("A daily input must keep its capability in the reviewed package.");
        await Reject(() => RulesPackageBuilder.BuildAsync(manifest,
            RulesDefinition.Parse(Rules.Replace("Shortfall > 0", "Missing > 0"))), "RULE_EXPRESSION_UNKNOWN_NAME");

        // The same field read as current and as daily needs two names; the rules use the alias.
        var both = manifest with
        {
            DataRequirements =
            [
                new("fillage", "well", "PumpFillage", "Current", "%", 3600, false),
                new("fillage-daily", "well", "PumpFillage", "Current", "%", 3600, false, null, "engineering.daily", "DailyFillage")
            ]
        };
        var aliased = await RulesPackageBuilder.BuildAsync(both,
            RulesDefinition.Parse(Rules.Replace("Shortfall > 0", "Shortfall > 0 or DailyFillage < 40")));
        if (aliased.Package.RulesModule is null ||
            aliased.Package.Manifest.DataRequirements.Count(input => input.Alias == "DailyFillage") != 1)
            throw new InvalidOperationException("An alias must survive packaging.");
        await Reject(() => RulesPackageBuilder.BuildAsync(both with
        {
            DataRequirements = [both.DataRequirements.First(), both.DataRequirements.Last() with { Alias = null }]
        }, definition), "GUIDED_RULES_INPUT_NAME_DUPLICATE");
        await Reject(() => RulesPackageBuilder.BuildAsync(both with
        {
            DataRequirements = [both.DataRequirements.First(), both.DataRequirements.Last() with { Alias = "9 lives" }]
        }, definition), "GUIDED_RULES_INPUT_ALIAS_INVALID");
    }

    private static async Task Reject(Func<object> action, string code) =>
        await Reject(() => { action(); return Task.CompletedTask; }, code);

    private static async Task Reject(Func<Task> action, string code)
    {
        try
        {
            await action();
        }
        catch (WellSiteAutoPilotException ex) when (ex.Code == code)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + code);
    }
}
