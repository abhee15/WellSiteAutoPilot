using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>
/// Runs a rules definition as a module. Preview and the worker both go through this class, so what an author tests
/// is what runs. Each input is read by its quantity name; settings come from the configured parameters, then the
/// asset's own overrides.
/// </summary>
public sealed class DeclarativeRulesModule : ILogicModuleV1
{
    public const string PayloadPath = "payload/definition.json";

    private readonly LogicModuleManifest _manifest;
    private readonly string _assetRole;

    private DeclarativeRulesModule(LogicModuleManifest manifest, RulesDefinition definition)
    {
        _manifest = manifest;
        Definition = definition;
        Identity = new(manifest.ModuleId, manifest.Version);
        _assetRole = manifest.DataRequirements.First().AssetRole;
    }

    public LogicModuleIdentity Identity { get; }
    public RulesDefinition Definition { get; }
    public IReadOnlyCollection<LogicDataRequirement> Inputs => _manifest.DataRequirements;

    public static bool UsesEntryPoint(LogicModuleManifest manifest) =>
        string.Equals(manifest.EntryPoint, RulesDefinition.EntryPoint, StringComparison.Ordinal);

    public static async Task<DeclarativeRulesModule> ReadAsync(LogicModuleManifest manifest, Stream definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(definition);
        if (!UsesEntryPoint(manifest))
            Fail("MODULE_DECLARATIVE_VERSION_UNSUPPORTED", "The declarative evaluator version is unsupported.");

        if (manifest.ManifestVersion != 1 || manifest.Runtime != LogicRuntimeKind.DotNet ||
            manifest.ExecutionProfile != ExecutionProfile.ScheduledOneShot ||
            manifest.AssetRequirements.Count != 1 ||
            manifest.AssetRequirements.Single() is not { MinimumCount: 1, MaximumCount: 1 } ||
            manifest.DataRequirements.Count == 0 ||
            manifest.DataRequirements.Any(input => input is not { Access: "Current", CanonicalUnit.Length: > 0 }) ||
            manifest.CommandRequirements.Count != 0 ||
            manifest.ParameterSchema is not { ValueKind: JsonValueKind.Object })
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID",
                "A rules module requires one asset, current inputs with units, and a settings schema.");

        var names = manifest.DataRequirements.Select(input => input.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            manifest.DataRequirements.Select(input => input.AssetRole).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", "Each input needs its own name on the same asset.");

        using var reader = new StreamReader(definition, leaveOpen: true);
        var json = await reader.ReadToEndAsync(cancellationToken);
        RulesDefinition parsed;
        try
        {
            parsed = RulesDefinition.Parse(json);
        }
        catch (RuleExpressionException ex)
        {
            throw new WellSiteAutoPilotException("MODULE_DECLARATIVE_CONTRACT_INVALID", FailureKind.Validation, ex.Message);
        }

        var issues = RulesEngine.Validate(parsed, names);
        if (issues.Count != 0)
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", issues[0].Message);

        await LogicModuleManifestValidator.ValidateParametersAsync(manifest, "{}", cancellationToken);
        return new(manifest, parsed);
    }

    public async ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ModuleIdentity != Identity || context.Assets.Count != 1 ||
            context.Assets.Single().AssetId == Guid.Empty ||
            !string.Equals(context.Assets.Single().Role, _assetRole, StringComparison.OrdinalIgnoreCase))
            Fail("MODULE_DECLARATIVE_CONTEXT_INVALID", "The execution does not match the reviewed asset role and module identity.");

        var asset = context.Assets.Single();
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var requirement in _manifest.DataRequirements)
        {
            var matches = context.Inputs.Where(input => input.AssetId == asset.AssetId &&
                string.Equals(input.RequirementId, requirement.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                Fail("MODULE_DECLARATIVE_INPUT_UNAVAILABLE", "A required input is unavailable.");
            LogicModuleInputValidation.Validate(matches[0], requirement, context.EvaluationTimeUtc);
            values[requirement.Name] = matches[0].Value;
        }

        var settings = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var json in new[] { context.ParametersJson, asset.ParameterOverridesJson })
        {
            await LogicModuleManifestValidator.ValidateParametersAsync(_manifest, json, cancellationToken);
            using var document = JsonDocument.Parse(json);
            try { LogicModuleManifestReader.ValidateUniqueFields(document.RootElement); }
            catch (JsonException)
            {
                Fail("CONFIGURED_LOGIC_PARAMETERS_INVALID", "The settings contain duplicate names.");
            }

            foreach (var parameter in Definition.Parameters)
            {
                if (!document.RootElement.TryGetProperty(parameter.Id, out var value)) continue;
                if (!value.TryGetDecimal(out var number))
                    Fail("CONFIGURED_LOGIC_PARAMETERS_INVALID", "A setting is outside the supported decimal range.");
                settings[parameter.Id] = number;
            }
        }

        RulesRunResult run;
        try
        {
            run = RulesEngine.Run(Definition, values, settings);
        }
        catch (RuleExpressionException ex)
        {
            throw new WellSiteAutoPilotException(
                ex.Code == "RULES_SETTING_OUT_OF_RANGE" ? "CONFIGURED_LOGIC_PARAMETERS_INVALID" : "MODULE_RULES_EVALUATION_FAILED",
                FailureKind.Validation, ex.Message);
        }

        var units = Definition.Calculations.Where(calculation => !string.IsNullOrWhiteSpace(calculation.Unit))
            .ToDictionary(calculation => calculation.Id, calculation => calculation.Unit!, StringComparer.Ordinal);
        var results = run.Calculations.Where(pair => units.ContainsKey(pair.Key))
            .Select(pair => new EngineeringResultValue(pair.Key, asset.AssetId, pair.Key, pair.Value, units[pair.Key]))
            .ToArray();
        RecommendationIntentDraft[] recommendations = run.Proposal is { } proposal
            ? [new RecommendationIntentDraft(run.OutcomeCode, asset.AssetId, run.Title ?? run.RuleId ?? run.OutcomeCode,
                proposal.Reason, proposal.Setpoint, proposal.SuggestedValue, proposal.Unit)]
            : Array.Empty<RecommendationIntentDraft>();
        return new(run.OutcomeCode, results, recommendations, [], new Dictionary<string, string>());
    }

    /// <summary>The settings schema a module manifest needs for this definition: one optional number per setting.</summary>
    public static string ParameterSchemaJson(RulesDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var properties = new Dictionary<string, Dictionary<string, object>>();
        foreach (var parameter in definition.Parameters)
        {
            var property = new Dictionary<string, object> { ["type"] = "number" };
            if (parameter.Minimum is { } min) property["minimum"] = min;
            if (parameter.Maximum is { } max) property["maximum"] = max;
            var unit = string.IsNullOrWhiteSpace(parameter.Unit) ? string.Empty : " " + parameter.Unit;
            var range = parameter.Minimum is { } lo && parameter.Maximum is { } hi ? $" Allowed {lo} to {hi}{unit}." : string.Empty;
            property["description"] = $"Default {parameter.Default}{unit}.{range}";
            properties[parameter.Id] = property;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        });
    }

    [DoesNotReturn]
    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(code, FailureKind.Validation, detail);
}
