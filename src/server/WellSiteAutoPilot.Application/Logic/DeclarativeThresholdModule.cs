using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>The first declarative artifact. Both preview and the worker use the same evaluator.</summary>
public sealed class DeclarativeThresholdModule : ILogicModuleV1
{
    public const string EntryPoint = "wsa.declarative.threshold.v1";
    public const string DifferenceEntryPoint = "wsa.declarative.difference.v1";
    public const string PayloadPath = "payload/definition.json";
    public const int MaximumDefinitionBytes = 8192;

    private static readonly JsonSerializerOptions DefinitionOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly LogicModuleManifest _manifest;
    private readonly LogicDataRequirement _input;
    private readonly LogicDataRequirement? _subtrahend;
    private readonly ThresholdDefinition _definition;

    private DeclarativeThresholdModule(LogicModuleManifest manifest, ThresholdDefinition definition)
    {
        _manifest = manifest;
        _subtrahend = manifest.EntryPoint == DifferenceEntryPoint
            ? manifest.DataRequirements.Single(requirement => requirement.Id == "tubing-pressure")
            : null;
        _input = _subtrahend is null ? manifest.DataRequirements.Single()
            : manifest.DataRequirements.Single(requirement => requirement.Id == "casing-pressure");
        _definition = definition;
        Identity = new(manifest.ModuleId, manifest.Version);
    }

    public LogicModuleIdentity Identity { get; }
    public LogicDataRequirement Input => _input;
    public LogicDataRequirement? Subtrahend => _subtrahend;
    public ThresholdDefinition Definition => _definition;

    public static bool UsesReservedEntryPoint(LogicModuleManifest manifest) =>
        manifest.EntryPoint?.StartsWith("wsa.declarative.", StringComparison.OrdinalIgnoreCase) == true;

    internal static async Task<DeclarativeThresholdModule> ReadAsync(LogicModuleManifest manifest,
        Stream definition, CancellationToken cancellationToken)
    {
        var difference = manifest.EntryPoint == DifferenceEntryPoint;
        if (manifest.EntryPoint != EntryPoint && !difference)
            Fail("MODULE_DECLARATIVE_VERSION_UNSUPPORTED", "The declarative evaluator version is unsupported.");

        if (manifest.ManifestVersion != 1 || manifest.Runtime != LogicRuntimeKind.DotNet ||
            manifest.ExecutionProfile != ExecutionProfile.ScheduledOneShot ||
            manifest.AssetRequirements.Count != 1 ||
            manifest.AssetRequirements.Single() is not { MinimumCount: 1, MaximumCount: 1 } ||
            manifest.DataRequirements.Count != (difference ? 2 : 1) ||
            manifest.DataRequirements.Any(input => input is not { Access: "Current", CanonicalUnit.Length: > 0 }) ||
            manifest.CommandRequirements.Count != 0 ||
            manifest.ParameterSchema is not { ValueKind: JsonValueKind.Object })
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", "A threshold module requires one asset, one current scalar input with a unit, and a numeric settings schema.");

        if (difference &&
            (!manifest.DataRequirements.Any(input => input is { Id: "casing-pressure", Quantity: "CasingPressure", CanonicalUnit: "psi", ValueSchema: null, RequiredCapability: null }) ||
             !manifest.DataRequirements.Any(input => input is { Id: "tubing-pressure", Quantity: "TubingPressure", CanonicalUnit: "psi", ValueSchema: null, RequiredCapability: null })))
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", "The pressure differential requires casing and tubing pressure in psi.");

        using var document = await JsonDocument.ParseAsync(definition, cancellationToken: cancellationToken);
        LogicModuleManifestReader.ValidateUniqueFields(document.RootElement);
        var parsed = document.RootElement.Deserialize<ThresholdDefinition>(DefinitionOptions);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Parameter) ||
            string.IsNullOrWhiteSpace(parsed.Title) || parsed.Title.Length > 160 ||
            string.IsNullOrWhiteSpace(parsed.Detail) || parsed.Detail.Length > 500 ||
            !IsOutcomeCode(parsed.OutcomeCode))
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", "The threshold definition requires a parameter, outcome code, title and detail.");

        var schema = manifest.ParameterSchema.GetValueOrDefault();
        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() != 1 ||
            !properties.TryGetProperty(parsed.Parameter, out var parameter) ||
            parameter.ValueKind != JsonValueKind.Object || !parameter.TryGetProperty("type", out var type) || type.GetString() != "number" ||
            !schema.TryGetProperty("additionalProperties", out var additional) ||
            additional.ValueKind != JsonValueKind.False ||
            schema.TryGetProperty("required", out var required) && required.GetArrayLength() != 0)
            Fail("MODULE_DECLARATIVE_CONTRACT_INVALID", "Declare exactly one optional numeric threshold and disallow other settings.");

        await LogicModuleManifestValidator.ValidateParametersAsync(manifest, "{}", cancellationToken);
        await LogicModuleManifestValidator.ValidateParametersAsync(manifest,
            JsonSerializer.Serialize(new Dictionary<string, decimal> { [parsed.Parameter] = parsed.Default }), cancellationToken);
        return new(manifest, parsed);
    }

    public async ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ModuleIdentity != Identity || context.Assets.Count != 1 ||
            context.Assets.Single().AssetId == Guid.Empty ||
            !string.Equals(context.Assets.Single().Role, _input.AssetRole, StringComparison.OrdinalIgnoreCase))
            Fail("MODULE_DECLARATIVE_CONTEXT_INVALID", "The execution does not match the reviewed asset role and module identity.");

        var asset = context.Assets.Single();
        if (context.Inputs.Count != (_subtrahend is null ? 1 : 2))
            Fail("MODULE_DECLARATIVE_INPUT_UNAVAILABLE", "A required scalar input is unavailable.");
        var reading = ReadInput(_input, context, asset.AssetId);
        decimal? difference = null;
        if (_subtrahend is not null)
        {
            var tubing = ReadInput(_subtrahend, context, asset.AssetId);
            if (reading < 0 || tubing < 0)
                Fail("INPUT_VALUE_INVALID", "Pressure readings must be nonnegative.");
            difference = reading - tubing;
        }

        var threshold = _definition.Default;
        foreach (var json in new[] { context.ParametersJson, asset.ParameterOverridesJson })
        {
            await LogicModuleManifestValidator.ValidateParametersAsync(_manifest, json, cancellationToken);
            using var settings = JsonDocument.Parse(json);
            try { LogicModuleManifestReader.ValidateUniqueFields(settings.RootElement); }
            catch (JsonException)
            {
                Fail("CONFIGURED_LOGIC_PARAMETERS_INVALID", "The threshold settings contain duplicate names.");
            }
            if (settings.RootElement.TryGetProperty(_definition.Parameter, out var value))
            {
                if (!value.TryGetDecimal(out threshold))
                    Fail("CONFIGURED_LOGIC_PARAMETERS_INVALID", "The threshold is outside the supported decimal range.");
            }
        }

        var triggered = difference is { } delta ? delta > threshold : reading < threshold;
        return new(triggered ? _definition.OutcomeCode : "THRESHOLD_NOT_MET",
            difference is { } measured ? [new("casing-tubing-differential", asset.AssetId, "CasingTubingDifferential", measured, "psi")] : [],
            triggered ? [new(_definition.OutcomeCode, asset.AssetId, _definition.Title, _definition.Detail)] : [],
            [], new Dictionary<string, string>());
    }

    private static decimal ReadInput(LogicDataRequirement requirement, LogicModuleExecutionContext context, Guid assetId)
    {
        var inputs = context.Inputs.Where(input => input.AssetId == assetId &&
            string.Equals(input.RequirementId, requirement.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (inputs.Length != 1)
            Fail("MODULE_DECLARATIVE_INPUT_UNAVAILABLE", "A required scalar input is unavailable.");
        LogicModuleInputValidation.Validate(inputs[0], requirement, context.EvaluationTimeUtc);
        return inputs[0].Value;
    }

    private static bool IsOutcomeCode(string? value) =>
        value is { Length: > 0 and <= 128 } && value != "THRESHOLD_NOT_MET" && char.IsAsciiLetterUpper(value[0]) &&
        value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');

    [DoesNotReturn]
    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(code, FailureKind.Validation, detail);

}

public sealed record ThresholdDefinition(string Parameter, decimal Default, string OutcomeCode, string Title, string Detail);
