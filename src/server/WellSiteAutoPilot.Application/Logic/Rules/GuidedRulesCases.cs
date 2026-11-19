using System.Text.Json;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>A scenario the author runs before submitting: input values, optional setting values, and what should happen.</summary>
public sealed record RulesCase(
    string Name,
    IReadOnlyDictionary<string, decimal> Values,
    IReadOnlyDictionary<string, decimal> Settings,
    string? ExpectedOutcome);

/// <summary>What one scenario did when it ran, kept as review evidence.</summary>
public sealed record RulesCaseEvidence(
    string Name,
    IReadOnlyDictionary<string, decimal> Values,
    IReadOnlyDictionary<string, decimal> Settings,
    string? ExpectedOutcome,
    string OutcomeCode,
    string? RuleId,
    RulesProposal? Proposal,
    IReadOnlyDictionary<string, decimal> Calculations);

public static class GuidedRulesCases
{
    public const int Maximum = 50;
    public const string NoOutcome = "NONE";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Runs every scenario. An expectation the author wrote must hold: a submission whose own checks fail is never sent
    /// for review. Returns the evidence a reviewer reads.
    /// </summary>
    public static IReadOnlyList<RulesCaseEvidence> Run(RulesDefinition definition, IReadOnlyCollection<string> inputNames,
        IReadOnlyList<RulesCase> cases)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cases);
        if (cases.Count is < 1 or > Maximum)
            throw Invalid("GUIDED_RULES_CASES_INVALID", "Provide between one and fifty scenarios.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<RulesCaseEvidence>(cases.Count);
        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > 60 || !names.Add(item.Name.Trim()))
                throw Invalid("GUIDED_RULES_CASES_INVALID", "Each scenario needs its own name of up to 60 characters.");
            if (inputNames.Any(name => !item.Values.ContainsKey(name)) ||
                item.Values.Keys.Any(name => !inputNames.Contains(name, StringComparer.Ordinal)))
                throw Invalid("GUIDED_RULES_CASES_INVALID", "Scenario '" + item.Name.Trim() + "' needs a value for each input.");
            RulesRunResult run;
            try
            {
                run = RulesEngine.Run(definition, item.Values, item.Settings);
            }
            catch (RuleExpressionException ex)
            {
                throw Invalid(ex.Code, "Scenario '" + item.Name.Trim() + "': " + ex.Message);
            }

            var expected = item.ExpectedOutcome;
            var actual = run.OutcomeCode == RulesDefinition.NoMatchOutcome ? NoOutcome : run.OutcomeCode;
            if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(expected, actual, StringComparison.Ordinal))
                throw Invalid("GUIDED_RULES_EXPECTATION_FAILED",
                    "Scenario '" + item.Name.Trim() + "' expected " + expected + " but produced " + actual + ".");
            evidence.Add(new(item.Name.Trim(), item.Values, item.Settings, string.IsNullOrWhiteSpace(expected) ? null : expected,
                run.OutcomeCode, run.RuleId, run.Proposal, run.Calculations));
        }

        return evidence;
    }

    public static string ToJson(IReadOnlyList<RulesCaseEvidence> evidence) => JsonSerializer.Serialize(evidence, Options);

    private static WellSiteAutoPilotException Invalid(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
