using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

public sealed record StructuredInputDefinition(LogicDataRequirement Requirement, string SchemaId, JsonElement Schema);
public sealed record StructuredOutputDefinition(string Id, string Quantity, string? Unit, string SchemaId, JsonElement Schema);
public sealed record StructuredModuleDefinition(LogicModuleIdentity Identity,
    IReadOnlyCollection<StructuredInputDefinition> Inputs, IReadOnlyCollection<StructuredOutputDefinition> Outputs,
    IReadOnlyCollection<LogicAssetRequirement>? AssetRequirements = null, JsonElement? ParameterSchema = null);

/// <summary>
/// Shared structured evaluation validator. No package loading, provider access or control execution.
/// The production worker supplies an already verified package and authorized observations.
/// Schemas come from the host's reviewed definition, never from the input value itself.
/// </summary>
public static class StructuredModuleOfflineHost
{
    public const int MaximumEnvelopeBytes = 262144;

    public static async Task<StructuredModuleDefinition> ReadDefinitionAsync(Stream reviewedManifest,
        CancellationToken cancellationToken = default)
    {
        var manifest = await LogicModuleManifestReader.ReadAsync(reviewedManifest, cancellationToken);
        if (manifest.ManifestVersion != 2) throw Failure("MODULE_CONTRACT_INVALID");
        return new(new(manifest.ModuleId, manifest.Version),
            manifest.DataRequirements.Select(r => new StructuredInputDefinition(r,
                r.ValueSchema!.SchemaId, r.ValueSchema.Schema)).ToArray(),
            manifest.OutputRequirements!.Select(r => new StructuredOutputDefinition(r.Id, r.Quantity, r.Unit,
                r.ValueSchema.SchemaId, r.ValueSchema.Schema)).ToArray(), manifest.AssetRequirements, manifest.ParameterSchema);
    }

    public static async Task<LogicModuleExecutionResultV2> ExecuteAsync(ILogicModuleV2 module,
        StructuredModuleDefinition definition, LogicModuleExecutionContextV2 context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (module.Identity != definition.Identity || context.ModuleIdentity != definition.Identity ||
            context.ExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(context.CorrelationId) ||
            context.EvaluationTimeUtc == default || context.Assets is null || context.Inputs is null ||
            context.Assets.Count is < 1 or > 64 || context.Inputs.Count is < 1 or > 64 ||
            definition.Inputs is null || definition.Outputs is null ||
            definition.Inputs.Count is < 1 or > 64 || definition.Outputs.Count is < 1 or > 64)
            throw Failure("MODULE_CONTEXT_INVALID");
        Bound(context);
        if (context.Assets.Any(a => a is null || a.AssetId == Guid.Empty || string.IsNullOrWhiteSpace(a.Role)) ||
            context.Assets.Select(a => (a.Role, a.AssetId)).Distinct().Count() != context.Assets.Count ||
            definition.Inputs.Any(d => d is null || d.Requirement is null || string.IsNullOrWhiteSpace(d.Requirement.Id) ||
                string.IsNullOrWhiteSpace(d.Requirement.AssetRole) || string.IsNullOrWhiteSpace(d.Requirement.Quantity) ||
                d.Requirement.MaximumAgeSeconds is <= 0) ||
            definition.Outputs.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id)) ||
            definition.Inputs.Select(d => d.Requirement.Id).Distinct(StringComparer.Ordinal).Count() != definition.Inputs.Count ||
            definition.Outputs.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definition.Outputs.Count)
            throw Failure("MODULE_CONTEXT_INVALID");
        if (definition.AssetRequirements is { } roles)
        {
            if (context.Assets.Any(a => !roles.Any(r => r.Role == a.Role)) || roles.Any(r =>
                context.Assets.Count(a => a.Role == r.Role) < r.MinimumCount ||
                context.Assets.Count(a => a.Role == r.Role) > r.MaximumCount))
                throw Failure("MODULE_CONTEXT_INVALID");
        }
        if (definition.ParameterSchema is { ValueKind: JsonValueKind.Object } parameters)
            await Assets.AssetAttributeSchemaValidator.ValidateValuesAsync(parameters.GetRawText(),
                context.ParametersJson, cancellationToken);
        var seenInputs = new HashSet<(string, Guid)>();
        foreach (var input in context.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declared = definition.Inputs.SingleOrDefault(d => d.Requirement.Id == input?.RequirementId);
            if (input is null || declared is null || input.Value is null || string.IsNullOrWhiteSpace(input.Source) ||
                !seenInputs.Add((input.RequirementId, input.AssetId)) ||
                !context.Assets.Any(a => a.AssetId == input.AssetId && a.Role == declared.Requirement.AssetRole))
                throw Failure("MODULE_CONTEXT_INVALID");
            LogicModuleInputValidation.ValidateObservation(input.Quantity, input.Unit, input.TimestampUtc,
                input.Quality, declared.Requirement, context.EvaluationTimeUtc);
            await StructuredValueValidation.ValidateAsync(input.Value, declared.SchemaId, declared.Schema, cancellationToken);
        }
        // Every declared requirement is supplied for every Asset bound to its role.
        foreach (var declared in definition.Inputs)
        {
            var assets = context.Assets.Where(a => a.Role == declared.Requirement.AssetRole).ToArray();
            if (assets.Length == 0 || assets.Any(a => !seenInputs.Contains((declared.Requirement.Id, a.AssetId))))
                throw Failure("MODULE_CONTEXT_INVALID");
        }

        var result = await module.ExecuteAsync(context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null) throw Failure("MODULE_RESULT_INVALID");
        if (result.FailureCode is { } code)
        {
            if (code.Length is < 1 or > 128 || !char.IsAsciiLetterUpper(code[0]) ||
                code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '_'))
                throw Failure("MODULE_RESULT_INVALID");
            throw Failure(code);
        }
        if (string.IsNullOrWhiteSpace(result.OutcomeCode) || result.EngineeringResults is null ||
            result.Recommendations is null || result.ControlIntents is null || result.StateUpdates is null ||
            result.EngineeringResults.Count > 64)
            throw Failure("MODULE_RESULT_INVALID");
        Bound(result);
        var seenResults = new HashSet<(string, Guid?)>();
        foreach (var output in result.EngineeringResults)
        {
            var declared = definition.Outputs.SingleOrDefault(d => d.Id == output?.Id);
            if (output is null || declared is null || output.Value is null ||
                output.Quantity != declared.Quantity || output.Unit != declared.Unit ||
                !seenResults.Add((output.Id, output.AssetId)) ||
                output.AssetId is { } assetId && !context.Assets.Any(a => a.AssetId == assetId))
                throw Failure("MODULE_RESULT_INVALID");
            await StructuredValueValidation.ValidateAsync(output.Value, declared.SchemaId, declared.Schema, cancellationToken);
        }
        return result;
    }

    private static void Bound<T>(T value)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value)) > MaximumEnvelopeBytes)
                throw Failure("MODULE_ENVELOPE_TOO_LARGE");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new WellSiteAutoPilotException("MODULE_ENVELOPE_INVALID", FailureKind.Validation,
                "The structured offline envelope could not be serialized.", innerException: error);
        }
    }

    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.Validation,
        "The structured offline evaluation did not satisfy its contract.");
}
