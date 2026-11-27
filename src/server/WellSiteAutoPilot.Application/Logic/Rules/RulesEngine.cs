using System.Globalization;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>One thing wrong with a definition, and where.</summary>
public sealed record RulesIssue(string Code, string Path, string Message);

/// <summary>A step recorded while a definition ran, so a result can be explained.</summary>
public sealed record RulesTraceStep(string Kind, string Id, string Detail);

/// <summary>What a run proposes: a setpoint, the value after limits, and why.</summary>
public sealed record RulesProposal(
    string Setpoint,
    decimal RequestedValue,
    decimal SuggestedValue,
    string? Unit,
    bool Limited,
    string Reason,
    string? Command = null);

public sealed record RulesRunResult(
    string OutcomeCode,
    string? RuleId,
    string? Title,
    RulesProposal? Proposal,
    IReadOnlyDictionary<string, decimal> Calculations,
    IReadOnlyList<RulesTraceStep> Trace);

/// <summary>Checks a definition against the module's inputs, and runs it. Pure: no I/O, no clock, no randomness.</summary>
public static class RulesEngine
{
    public static IReadOnlyList<RulesIssue> Validate(RulesDefinition definition, IReadOnlyCollection<string> inputNames)
    {
        var issues = new List<RulesIssue>();
        var kinds = new Dictionary<string, RuleValueKind>(StringComparer.Ordinal);
        foreach (var name in inputNames) kinds[name] = RuleValueKind.Number;

        if (definition.Parameters.Count > RulesDefinition.MaximumItems ||
            definition.Calculations.Count > RulesDefinition.MaximumItems ||
            definition.Rules.Count > RulesDefinition.MaximumItems)
            issues.Add(new("RULES_TOO_MANY_ITEMS", "", "A module may have at most 32 settings, calculations and rules."));

        for (var i = 0; i < definition.Parameters.Count; i++)
        {
            var p = definition.Parameters[i];
            var path = $"parameters[{i}]";
            if (!Declare(kinds, p.Id, path, issues)) continue;
            if (p.Minimum is { } min && p.Maximum is { } max && min > max)
                issues.Add(new("RULES_RANGE_INVALID", path, "The allowed minimum is above the allowed maximum."));
            if ((p.Minimum is { } lo && p.Default < lo) || (p.Maximum is { } hi && p.Default > hi))
                issues.Add(new("RULES_DEFAULT_OUT_OF_RANGE", path, "The default is outside the allowed range."));
        }

        for (var i = 0; i < definition.Calculations.Count; i++)
        {
            var path = $"calculations[{i}]";
            var calculation = definition.Calculations[i];
            // A calculation reads only what is declared before it, so order is the order of working.
            var kind = Check(calculation.Expression, kinds, path + ".expression", issues);
            if (kind is { } found && found != RuleValueKind.Number)
                issues.Add(new("RULE_EXPRESSION_TYPE", path + ".expression", "A calculation must produce a number."));
            Declare(kinds, calculation.Id, path, issues);
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < definition.Rules.Count; i++)
        {
            var rule = definition.Rules[i];
            var path = $"rules[{i}]";
            if (!IsIdentifier(rule.Id) || !ruleIds.Add(rule.Id))
                issues.Add(new("RULES_ID_INVALID", path, "A rule needs a unique name of letters, digits and underscores."));
            var when = Check(rule.When, kinds, path + ".when", issues);
            if (when is { } w && w != RuleValueKind.Boolean)
                issues.Add(new("RULE_EXPRESSION_TYPE", path + ".when", "A condition must be true or false."));
            if (string.IsNullOrWhiteSpace(rule.Reason) || rule.Reason.Length > 500)
                issues.Add(new("RULES_REASON_REQUIRED", path + ".reason", "Every rule needs a reason of up to 500 characters."));
            if (!IsOutcomeCode(rule.OutcomeCode))
                issues.Add(new("RULES_OUTCOME_INVALID", path + ".outcomeCode", "The outcome code is upper case letters, digits and underscores."));
            if (rule.Title is { Length: > 160 })
                issues.Add(new("RULES_TITLE_TOO_LONG", path + ".title", "The title is too long."));
            if (rule.Recommend is { } recommend) ValidateRecommendation(recommend, kinds, path + ".recommend", issues);
        }

        return issues;
    }

    private static void ValidateRecommendation(RulesRecommendation recommend, Dictionary<string, RuleValueKind> kinds,
        string path, List<RulesIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(recommend.Setpoint) || recommend.Setpoint.Length > 128)
            issues.Add(new("RULES_SETPOINT_REQUIRED", path + ".setpoint", "Name the setpoint to change."));
        if (!string.IsNullOrWhiteSpace(recommend.Command))
        {
            if (!IsIdentifier(recommend.Command.Trim()) || recommend.Command.Trim().Length > 64)
                issues.Add(new("RULES_COMMAND_INVALID", path + ".command", "A command is letters, digits and underscores, up to 64 characters."));
            if (string.IsNullOrWhiteSpace(recommend.Unit))
                issues.Add(new("RULES_COMMAND_UNIT_REQUIRED", path + ".unit", "A command needs the unit of the value it sends."));
        }

        var kind = Check(recommend.ValueExpression, kinds, path + ".valueExpression", issues);
        if (kind is { } found && found != RuleValueKind.Number)
            issues.Add(new("RULE_EXPRESSION_TYPE", path + ".valueExpression", "The suggested value must be a number."));
        foreach (var (name, field) in new[] { (recommend.MinimumFrom, "minimumFrom"), (recommend.MaximumFrom, "maximumFrom"), (recommend.MaximumChangeFrom, "maximumChangeFrom") })
        {
            if (!string.IsNullOrWhiteSpace(name) && (!kinds.TryGetValue(name, out var limitKind) || limitKind != RuleValueKind.Number))
                issues.Add(new("RULE_EXPRESSION_UNKNOWN_NAME", path + "." + field, "A limit must name a number in this module, such as a setting."));
        }

        if (recommend.Minimum is { } min && recommend.Maximum is { } max && min > max && string.IsNullOrWhiteSpace(recommend.MinimumFrom) && string.IsNullOrWhiteSpace(recommend.MaximumFrom))
            issues.Add(new("RULES_LIMIT_INVALID", path, "The minimum limit is above the maximum limit."));
        if (recommend.MaximumChange is { } change && change <= 0 && string.IsNullOrWhiteSpace(recommend.MaximumChangeFrom))
            issues.Add(new("RULES_LIMIT_INVALID", path + ".maximumChange", "The largest change must be above zero."));
        if ((recommend.MaximumChange is not null || !string.IsNullOrWhiteSpace(recommend.MaximumChangeFrom)) && string.IsNullOrWhiteSpace(recommend.CurrentValue))
            issues.Add(new("RULES_CURRENT_VALUE_REQUIRED", path + ".currentValue",
                "A largest change needs the current value to measure from."));
        if (!string.IsNullOrWhiteSpace(recommend.CurrentValue) &&
            (!kinds.TryGetValue(recommend.CurrentValue, out var currentKind) || currentKind != RuleValueKind.Number))
            issues.Add(new("RULE_EXPRESSION_UNKNOWN_NAME", path + ".currentValue", "The current value is not a number in this module."));
    }

    /// <summary>Runs the rules in order against the inputs and any setting values chosen; a setting not chosen keeps its default.</summary>
    public static RulesRunResult Run(RulesDefinition definition, IReadOnlyDictionary<string, decimal> inputs,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        var trace = new List<RulesTraceStep>();
        var calculated = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var input in inputs)
        {
            values[input.Key] = RuleValue.OfNumber(input.Value);
            trace.Add(new("input", input.Key, input.Value.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var parameter in definition.Parameters)
        {
            var value = settings.TryGetValue(parameter.Id, out var chosen) ? chosen : parameter.Default;
            if ((parameter.Minimum is { } min && value < min) || (parameter.Maximum is { } max && value > max))
                throw new RuleExpressionException("RULES_SETTING_OUT_OF_RANGE", $"'{parameter.Id}' is outside its allowed range.");
            values[parameter.Id] = RuleValue.OfNumber(value);
            trace.Add(new("setting", parameter.Id, value.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var calculation in definition.Calculations)
        {
            var result = RuleEvaluator.Evaluate(RuleExpressionParser.Parse(calculation.Expression), values);
            values[calculation.Id] = result;
            calculated[calculation.Id] = result.Number;
            trace.Add(new("calculation", calculation.Id, result.ToString()));
        }

        foreach (var rule in definition.Rules)
        {
            var matched = RuleEvaluator.Evaluate(RuleExpressionParser.Parse(rule.When), values).Boolean;
            trace.Add(new("rule", rule.Id, matched ? "matched" : "not matched"));
            if (!matched) continue;
            var proposal = rule.Recommend is { } recommend ? Propose(recommend, rule.Reason, values, trace) : null;
            return new(rule.OutcomeCode, rule.Id, rule.Title, proposal, calculated, trace);
        }

        return new(RulesDefinition.NoMatchOutcome, null, null, null, calculated, trace);
    }

    /// <summary>The proposal, or nothing when the setpoint already holds the value the rule would suggest.</summary>
    private static RulesProposal? Propose(RulesRecommendation recommend, string reason,
        Dictionary<string, RuleValue> values, List<RulesTraceStep> trace)
    {
        var requested = RuleEvaluator.Evaluate(RuleExpressionParser.Parse(recommend.ValueExpression), values).Number;
        var value = requested;
        var maximumChange = Limit(recommend.MaximumChangeFrom, recommend.MaximumChange, values);
        if (!string.IsNullOrWhiteSpace(recommend.CurrentValue) && maximumChange is { } change)
        {
            if (change <= 0)
                throw new RuleExpressionException("RULES_LIMIT_INVALID", "The largest change must be above zero.");
            var current = values[recommend.CurrentValue].Number;
            value = Math.Min(Math.Max(value, current - change), current + change);
        }

        var minimum = Limit(recommend.MinimumFrom, recommend.Minimum, values);
        var maximum = Limit(recommend.MaximumFrom, recommend.Maximum, values);
        if (minimum is { } min && maximum is { } top && min > top)
            throw new RuleExpressionException("RULES_LIMIT_INVALID", "The minimum limit is above the maximum limit.");
        if (minimum is { } low) value = Math.Max(value, low);
        if (maximum is { } max) value = Math.Min(value, max);
        var limited = value != requested;
        trace.Add(new("limit", recommend.Setpoint, limited ? "held to the limits" : "within the limits"));
        if (!string.IsNullOrWhiteSpace(recommend.CurrentValue) && values[recommend.CurrentValue].Number == value)
        {
            trace.Add(new("setpoint", recommend.Setpoint, "already at " + value.ToString(CultureInfo.InvariantCulture) + ", no change proposed"));
            return null;
        }

        return new(recommend.Setpoint, requested, value, recommend.Unit, limited, reason,
            string.IsNullOrWhiteSpace(recommend.Command) ? null : recommend.Command.Trim());
    }

    private static decimal? Limit(string? name, decimal? fixedValue, Dictionary<string, RuleValue> values) =>
        string.IsNullOrWhiteSpace(name) ? fixedValue : values[name].Number;

    private static RuleValueKind? Check(string expression, Dictionary<string, RuleValueKind> kinds, string path,
        List<RulesIssue> issues)
    {
        try
        {
            return RuleEvaluator.Check(RuleExpressionParser.Parse(expression), kinds);
        }
        catch (RuleExpressionException ex)
        {
            issues.Add(new(ex.Code, path, ex.Message));
            return null;
        }
    }

    private static bool Declare(Dictionary<string, RuleValueKind> kinds, string id, string path, List<RulesIssue> issues)
    {
        if (!IsIdentifier(id) || kinds.ContainsKey(id) ||
            RuleEvaluator.FunctionNames.Contains(id, StringComparer.OrdinalIgnoreCase) ||
            id.ToLowerInvariant() is "true" or "false" or "and" or "or" or "not")
        {
            issues.Add(new("RULES_ID_INVALID", path,
                "A name is letters, digits and underscores, starts with a letter, and is used once."));
            return false;
        }

        kinds[id] = RuleValueKind.Number;
        return true;
    }

    private static bool IsIdentifier(string? value) =>
        value is { Length: > 0 and <= 64 } && char.IsAsciiLetter(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool IsOutcomeCode(string? value) =>
        value is { Length: > 0 and <= 128 } && value != RulesDefinition.NoMatchOutcome &&
        char.IsAsciiLetterUpper(value[0]) &&
        value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}
