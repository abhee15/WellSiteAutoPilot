using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record GuidedThresholdDefinitionRequest(string Parameter, decimal Default, string OutcomeCode, string Title, string Detail);

/// <summary>A null Definition saves a contract-only draft: a typed manifest with no logic to preview or submit.</summary>
public sealed record SaveGuidedModuleDraftRequest(string ManifestJson, GuidedThresholdDefinitionRequest? Definition, int? ExpectedRevision,
    IReadOnlyList<GuidedSampleCaseRequest>? SampleCases = null, IReadOnlyList<GuidedSourceHintRequest>? SourceHints = null);

/// <summary>Where an author wants an input read from: a preference unless Required. Parameters is a JSON object as text.</summary>
public sealed record GuidedSourceHintRequest(string InputId, string? AdapterKey, string? OperationKey, bool Required, string? ParametersJson = null);

/// <summary>An authored sample. ExpectedOutcome is the author's assertion: null for none, NONE for not triggered, else the rule outcome code.</summary>
public sealed record GuidedSampleCaseRequest(string Name, decimal? Value, decimal? CasingPsi, decimal? TubingPsi, string? ExpectedOutcome);

public sealed record GuidedModuleDraftResponse(Guid Id, int Revision, string ManifestJson, GuidedThresholdDefinitionRequest? Definition,
    IReadOnlyList<GuidedSampleCaseRequest>? SampleCases = null, IReadOnlyList<GuidedSourceHintRequest>? SourceHints = null);

public sealed record GuidedPressureReadingRequest(decimal? CasingPsi, decimal? TubingPsi);

public sealed record PreviewGuidedModuleDraftRequest(int ExpectedRevision, decimal? Value, string ParametersJson, GuidedPressureReadingRequest? Pressure = null);

public sealed record GuidedModulePreviewResponse(int Revision, string PackageSha256, string? OutcomeCode,
    string? FailureCode, string? RecommendationTitle, string? RecommendationDetail, decimal? DifferentialPsi = null,
    string? ObservedOutcome = null);

public sealed record GuidedModuleReviewItemResponse(Guid DraftId, int Revision, string DisplayName, DateTimeOffset SubmittedAtUtc);

public sealed record GuidedModuleReviewResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc,
    string ReviewedManifestJson, GuidedThresholdDefinitionRequest Definition, IReadOnlyList<GuidedModuleReviewCaseResponse> Cases);

public sealed record GuidedModuleReviewCaseResponse(decimal? Value, string? OutcomeCode, string? FailureCode,
    GuidedPressureReadingRequest? Pressure = null, decimal? DifferentialPsi = null);

public sealed record ApproveGuidedModuleSubmissionRequest(string PackageSha256, string Reason);

public sealed record SubmitGuidedModuleDraftRequest(int ExpectedRevision, IReadOnlyList<decimal?>? Readings,
    IReadOnlyList<GuidedPressureReadingRequest>? PressureReadings = null);

public sealed record GuidedModuleSubmissionResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc);

public sealed record GuidedLivePreviewRequest(int ExpectedRevision, Guid AssetId);

public sealed record GuidedAssetCompatibilityResponse(IReadOnlyList<GuidedInputCompatibilityResponse> Inputs);

public sealed record GuidedInputCompatibilityResponse(string InputId, string Quantity, string Status, string? ReasonCode);

public sealed record GuidedLivePreviewResponse(int Revision, string PackageSha256, string? OutcomeCode, string? FailureCode,
    decimal? DifferentialPsi, IReadOnlyList<GuidedLiveReadingResponse> Readings);

public sealed record GuidedLiveReadingResponse(string InputId, string Quantity, decimal? Value, string Unit,
    DateTimeOffset TimestampUtc, string Quality, string? TimestampKind);

public sealed record GuidedHistoryPreviewRequest(int ExpectedRevision, Guid AssetId, string InputId,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, int MaximumSamples, GuidedSiteHistoryWindowRequest? SiteWindow = null);

public sealed record GuidedSiteHistoryWindowRequest(string EndLocal, string TimeZoneId, int DurationMinutes);

public sealed record GuidedHistoryPreviewResponse(string Quantity, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool Truncated, IReadOnlyList<GuidedHistorySampleResponse> Samples);

public sealed record GuidedHistorySampleResponse(decimal Value, string Unit, DateTimeOffset TimestampUtc, string Quality);

public sealed record GuidedQuantityDiscoveryRequest(int ExpectedRevision, Guid AssetId, string Capability,
    DateTimeOffset? StartUtc = null, DateTimeOffset? EndUtc = null, GuidedSiteHistoryWindowRequest? SiteWindow = null);

public sealed record GuidedQuantityDiscoveryResponse(bool Supported, IReadOnlyList<GuidedDiscoveredQuantityResponse> Quantities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EvidenceStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GuidedReportedWellTestResponse? WellTest);

public sealed record GuidedReportedWellTestResponse(DateTimeOffset? SampleDate, DateTimeOffset? TestEndDate,
    string? Status, string? Classification, string? Validation);

public sealed record GuidedDiscoveredQuantityResponse(string Quantity, string? Unit, string Status);

public sealed record GuidedProductionDaysPreviewRequest(int ExpectedRevision, Guid AssetId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public sealed record GuidedProductionDaysPreviewResponse(string? Unit, DateTimeOffset RetrievedAtUtc,
    IReadOnlyList<GuidedProductionDayResponse> Days);

public sealed record GuidedProductionDayResponse(DateTimeOffset Start, DateTimeOffset End, decimal? Value);

public sealed record GuidedSurfaceCardsPreviewResponse(string? Quality, DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<GuidedSurfaceCardResponse> Cards);

public sealed record GuidedSurfaceCardResponse(string Id, string Kind, string CapturedAtUtc,
    string PositionUnit, string LoadUnit, IReadOnlyList<decimal> Position, IReadOnlyList<decimal> Load);
