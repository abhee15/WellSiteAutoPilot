using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Logic.Rules;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class GuidedRulesDraftChecks
{
    private const string Rules = """
        {
          "parameters": [ { "id": "LowFillage", "unit": "%", "default": 70, "minimum": 0, "maximum": 100, "assignable": true } ],
          "calculations": [ { "id": "Shortfall", "expression": "LowFillage - PumpFillage", "unit": "%" } ],
          "rules": [ { "id": "Low", "when": "Shortfall > 0", "reason": "Fillage is below the limit.",
                       "outcomeCode": "LOW_FILLAGE", "title": null, "recommend": null } ]
        }
        """;

    public static void Run()
    {
        var manifest = new LogicModuleManifest(1, "guided.rules-draft", "1.0.0", "Rules draft", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, "contract.unset", "1.1.0",
            [new("well", 1, 1, null, [])],
            [new("fillage", "well", "PumpFillage", "Current", "%", 3600, false)], []);

        GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest));
        GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest, RulesJson: Rules));

        Reject(manifest, Rules.Replace("Shortfall > 0", "Missing > 0"), "RULE_EXPRESSION_UNKNOWN_NAME");
        Reject(manifest, Rules.Replace("\"rules\": [", "\"rulez\": ["), "RULES_DEFINITION_INVALID");
        Reject(manifest, "not json", "RULES_DEFINITION_INVALID");

        GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest, RulesJson: Rules, ScenariosJson: "[]"));
        foreach (var bad in new[] { "{}", "not json" })
        {
            try
            {
                GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest, RulesJson: Rules, ScenariosJson: bad));
                throw new InvalidOperationException("Scenarios that are not a list were accepted.");
            }
            catch (WellSiteAutoPilotException ex) when (ex.Code == "GUIDED_SCENARIOS_INVALID")
            {
            }
        }

        var threshold = new ThresholdDefinition("threshold", 70, "LOW_FILLAGE", "Low", "Review.");
        try
        {
            GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest, threshold, RulesJson: Rules));
            throw new InvalidOperationException("A draft cannot hold a threshold rule and rules.");
        }
        catch (WellSiteAutoPilotException ex) when (ex.Code == "GUIDED_RULES_INVALID")
        {
        }
    }

    private static void Reject(LogicModuleManifest manifest, string json, string code)
    {
        try
        {
            GuidedRulesDraft.Validate(new GuidedModuleDraftContent(manifest, RulesJson: json));
        }
        catch (WellSiteAutoPilotException ex) when (ex.Code == code)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + code);
    }
}
