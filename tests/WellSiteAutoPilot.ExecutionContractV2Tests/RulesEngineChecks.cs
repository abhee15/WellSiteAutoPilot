using WellSiteAutoPilot.Application.Logic.Rules;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class RulesEngineChecks
{
    private const string Json = """
        {
          "parameters": [
            { "id": "LowFillage", "unit": "%", "default": 70, "minimum": 0, "maximum": 100, "assignable": true },
            { "id": "SpeedStep", "unit": "spm", "default": 0.5, "minimum": 0.1, "maximum": 2, "assignable": true },
            { "id": "MinSpeed", "unit": "spm", "default": 3, "minimum": null, "maximum": null, "assignable": false }
          ],
          "calculations": [
            { "id": "Shortfall", "expression": "LowFillage - PumpFillage", "unit": "%" }
          ],
          "rules": [
            {
              "id": "SlowDown",
              "when": "Shortfall > 0 and CurrentSpeed > MinSpeed",
              "reason": "Fillage is below the limit, so slow the pump to let it fill.",
              "outcomeCode": "SLOW_PUMP",
              "title": "Reduce pump speed",
              "recommend": {
                "setpoint": "PumpSpeed",
                "valueExpression": "CurrentSpeed - SpeedStep * 4",
                "unit": "spm",
                "currentValue": "CurrentSpeed",
                "minimum": null,
                "maximum": 12,
                "maximumChange": 1
              }
            }
          ]
        }
        """;

    public static void Run()
    {
        var definition = RulesDefinition.Parse(Json);
        var inputs = new[] { "PumpFillage", "CurrentSpeed" };
        var issues = RulesEngine.Validate(definition, inputs);
        if (issues.Count != 0)
            throw new InvalidOperationException("A valid definition was rejected: " + issues[0].Code + " " + issues[0].Path);

        var again = RulesDefinition.Parse(definition.ToJson());
        if (again.ToJson() != definition.ToJson())
            throw new InvalidOperationException("A definition must survive a round trip.");

        var none = new Dictionary<string, decimal>();
        var matched = RulesEngine.Run(definition,
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m }, none);
        if (matched.OutcomeCode != "SLOW_PUMP" || matched.RuleId != "SlowDown" ||
            matched.Calculations["Shortfall"] != 15m ||
            matched.Proposal is not { RequestedValue: 4m, SuggestedValue: 5m, Limited: true, Setpoint: "PumpSpeed" })
            throw new InvalidOperationException("The rule must propose a change held to the largest change.");
        if (!matched.Trace.Any(step => step.Kind == "rule" && step.Id == "SlowDown" && step.Detail == "matched"))
            throw new InvalidOperationException("The trace must record the rule that matched.");

        var chosen = RulesEngine.Run(definition,
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m },
            new Dictionary<string, decimal> { ["SpeedStep"] = 0.1m });
        if (chosen.Proposal is not { RequestedValue: 5.6m, SuggestedValue: 5.6m, Limited: false })
            throw new InvalidOperationException("A chosen setting must replace its default.");

        var clear = RulesEngine.Run(definition,
            new Dictionary<string, decimal> { ["PumpFillage"] = 90m, ["CurrentSpeed"] = 6m }, none);
        if (clear.OutcomeCode != RulesDefinition.NoMatchOutcome || clear.Proposal is not null)
            throw new InvalidOperationException("No match must produce no proposal.");

        var atFloor = RulesEngine.Run(definition,
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 3m }, none);
        if (atFloor.OutcomeCode != RulesDefinition.NoMatchOutcome)
            throw new InvalidOperationException("The rule must not match at the minimum speed.");

        // A limit that holds the suggestion at the current value proposes nothing, but the rule still decided.
        var held = RulesEngine.Run(RulesDefinition.Parse(Json.Replace("CurrentSpeed - SpeedStep * 4", "CurrentSpeed + 1").Replace("\"maximum\": 12", "\"maximum\": 6")),
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m }, none);
        if (held.OutcomeCode != "SLOW_PUMP" || held.Proposal is not null ||
            !held.Trace.Any(step => step.Kind == "setpoint" && step.Detail.Contains("no change", StringComparison.Ordinal)))
            throw new InvalidOperationException("A suggestion equal to the current value must not be proposed.");

        // A limit may name a number in the module, such as a setting, so each well can tune it.
        var tuned = Json.Replace("\"maximum\": 12", "\"maximum\": null, \"maximumFrom\": \"MinSpeed\"");
        var named = RulesEngine.Run(RulesDefinition.Parse(tuned),
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 4m }, none);
        if (named.Proposal is not { SuggestedValue: 3m, Limited: true })
            throw new InvalidOperationException("A named limit must hold the suggestion.");
        ExpectIssue(Json.Replace("\"maximum\": 12", "\"maximum\": null, \"maximumFrom\": \"Missing\""), inputs,
            "RULE_EXPRESSION_UNKNOWN_NAME");

        // A maximum below the reachable window must not push the value past the largest change: nothing is proposed.
        var unreachable = RulesEngine.Run(RulesDefinition.Parse(Json.Replace("\"maximum\": 12", "\"maximum\": 2")),
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m }, none);
        if (unreachable.OutcomeCode != "SLOW_PUMP" || unreachable.Proposal is not null)
            throw new InvalidOperationException("A limit outside the largest change must propose nothing.");

        // A move smaller than the deadband is not worth a command; rounding snaps the value to the equipment's step.
        var inDeadband = RulesEngine.Run(RulesDefinition.Parse(Json.Replace("\"maximumChange\": 1", "\"maximumChange\": 1, \"deadband\": 2")),
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m }, none);
        if (inDeadband.OutcomeCode != "SLOW_PUMP" || inDeadband.Proposal is not null ||
            !inDeadband.Trace.Any(step => step.Kind == "setpoint" && step.Detail.Contains("deadband", StringComparison.Ordinal)))
            throw new InvalidOperationException("A move inside the deadband must propose nothing.");
        var rounded = RulesEngine.Run(RulesDefinition.Parse(Json.Replace("\"maximumChange\": 1", "\"maximumChange\": 1, \"increment\": 0.5")),
            new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6.3m }, none);
        if (rounded.Proposal is not { SuggestedValue: 5.5m })
            throw new InvalidOperationException("The value must be rounded to the increment.");
        ExpectIssue(Json.Replace("\"maximumChange\": 1", "\"maximumChange\": 1, \"deadband\": 0"), inputs, "RULES_LIMIT_INVALID");
        ExpectIssue(Json.Replace("\"maximumChange\": 1", "\"maximumChange\": 1, \"increment\": -1"), inputs, "RULES_LIMIT_INVALID");

        // A name the run did not supply is a coded failure, never an unhandled KeyNotFoundException.
        try
        {
            RulesEngine.Run(RulesDefinition.Parse(Json.Replace("\"currentValue\": \"CurrentSpeed\"", "\"currentValue\": \"Ghost\"")),
                new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m }, none);
            throw new InvalidOperationException("A missing current value must fail.");
        }
        catch (RuleExpressionException ex) when (ex.Code == "RULES_VALUE_MISSING")
        {
        }

        try
        {
            RulesEngine.Run(definition, new Dictionary<string, decimal> { ["PumpFillage"] = 55m, ["CurrentSpeed"] = 6m },
                new Dictionary<string, decimal> { ["LowFillage"] = 150m });
            throw new InvalidOperationException("A setting outside its range must fail.");
        }
        catch (RuleExpressionException ex) when (ex.Code == "RULES_SETTING_OUT_OF_RANGE")
        {
        }

        ExpectIssue(Json.Replace("Shortfall > 0", "Shortfall + 1"), inputs, "RULE_EXPRESSION_TYPE");
        ExpectIssue(Json.Replace("CurrentSpeed > MinSpeed", "Speed > MinSpeed"), inputs, "RULE_EXPRESSION_UNKNOWN_NAME");
        ExpectIssue(Json.Replace("\"reason\": \"Fillage is below the limit, so slow the pump to let it fill.\"", "\"reason\": \"\""),
            inputs, "RULES_REASON_REQUIRED");
        ExpectIssue(Json.Replace("\"id\": \"SpeedStep\"", "\"id\": \"LowFillage\""), inputs, "RULES_ID_INVALID");
        ExpectIssue(Json.Replace("\"default\": 70", "\"default\": 170"), inputs, "RULES_DEFAULT_OUT_OF_RANGE");
        ExpectIssue(Json.Replace("\"outcomeCode\": \"SLOW_PUMP\"", "\"outcomeCode\": \"slow\""), inputs, "RULES_OUTCOME_INVALID");
        ExpectIssue(Json.Replace("\"maximumChange\": 1", "\"maximumChange\": 0"), inputs, "RULES_LIMIT_INVALID");
        ExpectIssue(Json.Replace("\"currentValue\": \"CurrentSpeed\"", "\"currentValue\": null"), inputs, "RULES_CURRENT_VALUE_REQUIRED");

        try
        {
            RulesDefinition.Parse("""{ "parameters": [], "calculations": [], "rules": [], "extra": 1 }""");
            throw new InvalidOperationException("An unknown member must be rejected.");
        }
        catch (RuleExpressionException ex) when (ex.Code == "RULES_DEFINITION_INVALID")
        {
        }
    }

    private static void ExpectIssue(string json, string[] inputs, string code)
    {
        var issues = RulesEngine.Validate(RulesDefinition.Parse(json), inputs);
        if (!issues.Any(issue => issue.Code == code))
            throw new InvalidOperationException("Expected " + code + " but got " + string.Join(", ", issues.Select(issue => issue.Code)));
    }
}
