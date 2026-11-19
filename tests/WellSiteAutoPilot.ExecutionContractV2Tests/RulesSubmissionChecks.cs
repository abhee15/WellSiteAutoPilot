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

        var daily = manifest with { DataRequirements = [new("fillage", "well", "PumpFillage", "Daily", "%", 3600, false)] };
        await Reject(() => RulesPackageBuilder.BuildAsync(daily, definition), "GUIDED_RULES_INPUT_UNSUPPORTED");
        await Reject(() => RulesPackageBuilder.BuildAsync(manifest,
            RulesDefinition.Parse(Rules.Replace("Shortfall > 0", "Missing > 0"))), "RULE_EXPRESSION_UNKNOWN_NAME");
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
