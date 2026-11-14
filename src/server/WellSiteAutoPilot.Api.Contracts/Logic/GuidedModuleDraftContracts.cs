namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record GuidedThresholdDefinitionRequest(string Parameter, decimal Default, string OutcomeCode, string Title, string Detail);

public sealed record SaveGuidedModuleDraftRequest(string ManifestJson, GuidedThresholdDefinitionRequest Definition, int? ExpectedRevision);

public sealed record GuidedModuleDraftResponse(Guid Id, int Revision, string ManifestJson, GuidedThresholdDefinitionRequest Definition);

public sealed record GuidedPressureReadingRequest(decimal? CasingPsi, decimal? TubingPsi);

public sealed record PreviewGuidedModuleDraftRequest(int ExpectedRevision, decimal? Value, string ParametersJson, GuidedPressureReadingRequest? Pressure = null);

public sealed record GuidedModulePreviewResponse(int Revision, string PackageSha256, string? OutcomeCode,
    string? FailureCode, string? RecommendationTitle, string? RecommendationDetail, decimal? DifferentialPsi = null);

public sealed record GuidedModuleReviewItemResponse(Guid DraftId, int Revision, string DisplayName, DateTimeOffset SubmittedAtUtc);

public sealed record GuidedModuleReviewResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc,
    string ReviewedManifestJson, GuidedThresholdDefinitionRequest Definition, IReadOnlyList<GuidedModuleReviewCaseResponse> Cases);

public sealed record GuidedModuleReviewCaseResponse(decimal? Value, string? OutcomeCode, string? FailureCode,
    GuidedPressureReadingRequest? Pressure = null, decimal? DifferentialPsi = null);

public sealed record ApproveGuidedModuleSubmissionRequest(string PackageSha256, string Reason);

public sealed record SubmitGuidedModuleDraftRequest(int ExpectedRevision, IReadOnlyList<decimal?>? Readings,
    IReadOnlyList<GuidedPressureReadingRequest>? PressureReadings = null);

public sealed record GuidedModuleSubmissionResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc);
