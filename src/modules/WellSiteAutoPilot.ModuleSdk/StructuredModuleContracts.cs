using System.Text.Json;

namespace WellSiteAutoPilot.ModuleSdk;

/// <summary>A schema-identified JSON value. JSON numbers remain numbers; arrays and records retain their shape.</summary>
public sealed record StructuredValue(string SchemaId, JsonElement Data);

public sealed record StructuredInputValue(
    string RequirementId, Guid AssetId, string Quantity, StructuredValue Value,
    string? Unit, DateTimeOffset TimestampUtc, string Quality, string Source)
{
    public DateTimeOffset? RetrievedAtUtc { get; init; }
    public string? TimestampKind { get; init; }
}

public sealed record StructuredResultValue(
    string Id, Guid? AssetId, string Quantity, StructuredValue Value, string? Unit);

/// <summary>
/// Module-reported scalar evidence. It is NOT a host-computed or independently verified measurement.
/// </summary>
public sealed record StructuredStepMetricV1(string Id, decimal Value, string? Unit);

/// <summary>
/// A completed code-reported calculation step. References must point to reviewed requirements or prior steps;
/// no implicit dependency is inferred from the module implementation.
/// </summary>
public sealed record StructuredCalculationStepV1(
    string Id, string Kind, IReadOnlyCollection<string> RequirementIds,
    IReadOnlyCollection<string> PriorStepIds, IReadOnlyCollection<StructuredStepMetricV1> Metrics);

/// <summary>Optional versioned offline calculation evidence. The host validates structure, not algorithm truth.</summary>
public sealed record StructuredCalculationTraceV1(
    int Version, IReadOnlyCollection<StructuredCalculationStepV1> Steps);

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
    /// <summary>Optional module-reported trace; validated and returned only for successful offline evaluation.</summary>
    public StructuredCalculationTraceV1? CalculationTrace { get; init; }
}

/// <summary>
/// Structured contract. Hosted execution requires a reviewed V2 manifest and supported source capability.
/// Do not also implement V1 to bypass that capability check.
/// </summary>
public interface ILogicModuleV2
{
    LogicModuleIdentity Identity { get; }

    ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(
        LogicModuleExecutionContextV2 context, CancellationToken cancellationToken = default);
}
