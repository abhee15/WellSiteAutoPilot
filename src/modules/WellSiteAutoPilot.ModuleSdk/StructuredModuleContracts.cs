using System.Text.Json;

namespace WellSiteAutoPilot.ModuleSdk;

/// <summary>A schema-identified JSON value. JSON numbers remain numbers; arrays and records retain their shape.</summary>
public sealed record StructuredValue(string SchemaId, JsonElement Data);

public sealed record StructuredInputValue(
    string RequirementId, Guid AssetId, string Quantity, StructuredValue Value,
    string? Unit, DateTimeOffset TimestampUtc, string Quality, string Source);

public sealed record StructuredResultValue(
    string Id, Guid? AssetId, string Quantity, StructuredValue Value, string? Unit);

public sealed record LogicModuleExecutionContextV2(
    Guid ExecutionId, LogicModuleIdentity ModuleIdentity, string CorrelationId,
    DateTimeOffset EvaluationTimeUtc, string ParametersJson,
    IReadOnlyCollection<LogicAssetContext> Assets,
    IReadOnlyCollection<StructuredInputValue> Inputs);

public sealed record LogicModuleExecutionResultV2(
    string OutcomeCode,
    IReadOnlyCollection<StructuredResultValue> EngineeringResults,
    IReadOnlyCollection<RecommendationIntentDraft> Recommendations,
    IReadOnlyCollection<ControlIntentDraft> ControlIntents,
    IReadOnlyDictionary<string, string> StateUpdates)
{
    public string? FailureCode { get; init; }
}

/// <summary>
/// Structured authoring contract. Production V1 hosts deliberately cannot load this interface.
/// Do not also implement V1 to bypass that capability check.
/// </summary>
public interface ILogicModuleV2
{
    LogicModuleIdentity Identity { get; }

    ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(
        LogicModuleExecutionContextV2 context, CancellationToken cancellationToken = default);
}
