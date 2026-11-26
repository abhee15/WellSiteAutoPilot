using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>
/// The authored rules of a draft, kept beside the portable manifest as the definition's own JSON. A draft is only
/// saved when every expression checks against the module's declared inputs, so what is stored can always be run.
/// </summary>
public static class GuidedRulesDraft
{
    public static void Validate(GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.RulesJson is not { } json) return;
        if (content.Definition is not null)
            throw Invalid("GUIDED_RULES_INVALID", "A draft holds either a threshold rule or rules, not both.");

        RulesDefinition definition;
        try
        {
            definition = RulesDefinition.Parse(json);
        }
        catch (RuleExpressionException ex)
        {
            throw Invalid(ex.Code, ex.Message);
        }

        var names = content.Manifest.DataRequirements.Select(input => input.Name).ToArray();
        var issues = RulesEngine.Validate(definition, names);
        if (issues.Count != 0)
            throw Invalid(issues[0].Code, issues[0].Path.Length == 0 ? issues[0].Message : issues[0].Path + ": " + issues[0].Message);
    }

    private static WellSiteAutoPilotException Invalid(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
