using WellSiteAutoPilot.Application.Logic.Rules;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class RuleExpressionChecks
{
    public static void Run()
    {
        var kinds = new Dictionary<string, RuleValueKind>(StringComparer.Ordinal)
        {
            ["Fillage"] = RuleValueKind.Number,
            ["MinSpeed"] = RuleValueKind.Number,
            ["Enabled"] = RuleValueKind.Boolean
        };
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["Fillage"] = RuleValue.OfNumber(62.5m),
            ["MinSpeed"] = RuleValue.OfNumber(4m),
            ["Enabled"] = RuleValue.OfBoolean(true)
        };

        Expect("MinSpeed - 1", kinds, values, RuleValueKind.Number, "3");
        Expect("Fillage < 70 and Enabled", kinds, values, RuleValueKind.Boolean, "true");
        Expect("not (Fillage >= 70) or false", kinds, values, RuleValueKind.Boolean, "true");
        Expect("if(Fillage > 90, 1, 2) * 3", kinds, values, RuleValueKind.Number, "6");
        Expect("max(MinSpeed, 6) + min(1, 2)", kinds, values, RuleValueKind.Number, "7");
        Expect("clamp(Fillage, 0, 50)", kinds, values, RuleValueKind.Number, "50");
        Expect("round(avg(1, 2), 0)", kinds, values, RuleValueKind.Number, "2");
        Expect("-MinSpeed + 10 / 4", kinds, values, RuleValueKind.Number, "-1.5");

        var refs = RuleExpressionParser.References(RuleExpressionParser.Parse("Fillage + MinSpeed * Fillage"));
        if (!refs.SequenceEqual(["Fillage", "MinSpeed"]))
            throw new InvalidOperationException("References must list each name once in first-use order.");

        ExpectFailure("", kinds, "RULE_EXPRESSION_EMPTY");
        ExpectFailure("Fillage +", kinds, "RULE_EXPRESSION_SYNTAX");
        ExpectFailure("1 < 2 < 3", kinds, "RULE_EXPRESSION_SYNTAX");
        ExpectFailure("Unknown + 1", kinds, "RULE_EXPRESSION_UNKNOWN_NAME");
        ExpectFailure("foo(1)", kinds, "RULE_EXPRESSION_UNKNOWN_FUNCTION");
        ExpectFailure("abs(1, 2)", kinds, "RULE_EXPRESSION_ARGUMENTS");
        ExpectFailure("Fillage and Enabled", kinds, "RULE_EXPRESSION_TYPE");
        ExpectFailure("if(Enabled, 1, true)", kinds, "RULE_EXPRESSION_TYPE");
        ExpectFailure(new string('1', 600), kinds, "RULE_EXPRESSION_TOO_LONG");
        ExpectFailure(new string('(', 40) + "1" + new string(')', 40), kinds, "RULE_EXPRESSION_TOO_COMPLEX");

        try
        {
            RuleEvaluator.Evaluate(RuleExpressionParser.Parse("1 / (MinSpeed - 4)"), values);
            throw new InvalidOperationException("Division by zero must fail.");
        }
        catch (RuleExpressionException ex) when (ex.Code == "RULE_EXPRESSION_DIVIDE_BY_ZERO")
        {
        }
    }

    private static void Expect(string text, Dictionary<string, RuleValueKind> kinds,
        Dictionary<string, RuleValue> values, RuleValueKind kind, string expected)
    {
        var node = RuleExpressionParser.Parse(text);
        var actualKind = RuleEvaluator.Check(node, kinds);
        var actual = RuleEvaluator.Evaluate(node, values).ToString();
        if (actualKind != kind || actual != expected)
            throw new InvalidOperationException($"'{text}' gave {actualKind} {actual}, expected {kind} {expected}.");
    }

    private static void ExpectFailure(string text, Dictionary<string, RuleValueKind> kinds, string code)
    {
        try
        {
            RuleEvaluator.Check(RuleExpressionParser.Parse(text), kinds);
        }
        catch (RuleExpressionException ex) when (ex.Code == code)
        {
            return;
        }

        throw new InvalidOperationException($"'{text}' should fail with {code}.");
    }
}
