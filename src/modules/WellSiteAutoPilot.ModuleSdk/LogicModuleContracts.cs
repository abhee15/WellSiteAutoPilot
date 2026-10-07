namespace WellSiteAutoPilot.ModuleSdk;

public sealed record LogicModuleIdentity(
    string ModuleId,
    string Version);

public sealed record LogicAssetContext(
    string Role,
    Guid AssetId,
    string ParameterOverridesJson);

public sealed record LogicInputValue(
    string RequirementId,
    Guid AssetId,
    string Quantity,
    decimal Value,
    string Unit,
    DateTimeOffset TimestampUtc,
    string Quality,
    string Source);

public sealed record LogicModuleExecutionContext(
    Guid ExecutionId,
    LogicModuleIdentity ModuleIdentity,
    string CorrelationId,
    DateTimeOffset EvaluationTimeUtc,
    string ParametersJson,
    IReadOnlyCollection<LogicAssetContext> Assets,
    IReadOnlyCollection<LogicInputValue> Inputs);

public sealed record EngineeringResultValue(
    string Id,
    Guid? AssetId,
    string Quantity,
    decimal Value,
    string Unit);

public sealed record RecommendationIntentDraft(
    string Code,
    Guid AssetId,
    string Title,
    string Detail,
    string? Quantity = null,
    decimal? SuggestedValue = null,
    string? Unit = null);

public sealed record ControlIntentDraft(
    string Code,
    Guid AssetId,
    string Command,
    string? Quantity,
    decimal? RequestedValue,
    string? Unit,
    string ReasonCode);

public sealed record LogicModuleExecutionResult(
    string OutcomeCode,
    IReadOnlyCollection<EngineeringResultValue> EngineeringResults,
    IReadOnlyCollection<RecommendationIntentDraft> Recommendations,
    IReadOnlyCollection<ControlIntentDraft> ControlIntents,
    IReadOnlyDictionary<string, string> StateUpdates);

public interface ILogicModuleV1
{
    LogicModuleIdentity Identity { get; }

    ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context,
        CancellationToken cancellationToken = default);
}
