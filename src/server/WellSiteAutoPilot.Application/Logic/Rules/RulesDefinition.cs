using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>A setting of the module: a number with a unit, a default, and the range an author or assigner may choose from.</summary>
public sealed record RulesParameter(
    string Id,
    string? Unit,
    decimal Default,
    decimal? Minimum,
    decimal? Maximum,
    bool Assignable);

/// <summary>A value worked out from inputs, settings and earlier calculations.</summary>
public sealed record RulesCalculation(string Id, string Expression, string? Unit);

/// <summary>
/// A proposal to change a setpoint. The value is worked out by an expression and then held inside the limits:
/// never below Minimum, never above Maximum, and never further from the current value than MaximumChange. Each limit is either a fixed
/// number or the name of a number in the module, usually a setting, so it can be tuned per well; a named limit wins.
/// </summary>
public sealed record RulesRecommendation(
    string Setpoint,
    string ValueExpression,
    string? Unit,
    string? CurrentValue,
    decimal? Minimum,
    decimal? Maximum,
    decimal? MaximumChange,
    string? MinimumFrom = null,
    string? MaximumFrom = null,
    string? MaximumChangeFrom = null);

/// <summary>One ordered rule. The first rule whose condition is true decides the outcome.</summary>
public sealed record RulesRule(
    string Id,
    string When,
    string Reason,
    string OutcomeCode,
    string? Title,
    RulesRecommendation? Recommend);

/// <summary>
/// The whole logic of a rules module: settings, calculations and ordered rules. Inputs are not listed here; they
/// come from the module's declared data requirements.
/// </summary>
public sealed record RulesDefinition(
    IReadOnlyList<RulesParameter> Parameters,
    IReadOnlyList<RulesCalculation> Calculations,
    IReadOnlyList<RulesRule> Rules)
{
    public const string EntryPoint = "wsa.declarative.rules.v1";
    public const string NoMatchOutcome = "NO_RULE_MATCHED";
    public const int MaximumDefinitionBytes = 32768;
    public const int MaximumItems = 32;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false
    };

    public static RulesDefinition Empty { get; } = new([], [], []);

    public static RulesDefinition Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumDefinitionBytes)
            throw new RuleExpressionException("RULES_DEFINITION_INVALID", "The definition is empty or too large.");
        try
        {
            return JsonSerializer.Deserialize<RulesDefinition>(json, Options)
                   ?? throw new RuleExpressionException("RULES_DEFINITION_INVALID", "The definition is empty.");
        }
        catch (JsonException)
        {
            throw new RuleExpressionException("RULES_DEFINITION_INVALID", "The definition is not valid.");
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
