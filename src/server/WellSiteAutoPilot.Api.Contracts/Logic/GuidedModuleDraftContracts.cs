namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record GuidedThresholdDefinitionRequest(string Parameter, decimal Default, string OutcomeCode, string Title, string Detail);

public sealed record SaveGuidedModuleDraftRequest(string ManifestJson, GuidedThresholdDefinitionRequest Definition, int? ExpectedRevision);

public sealed record GuidedModuleDraftResponse(Guid Id, int Revision, string ManifestJson, GuidedThresholdDefinitionRequest Definition);

public sealed record PreviewGuidedModuleDraftRequest(int ExpectedRevision, decimal? Value, string ParametersJson);

public sealed record GuidedModulePreviewResponse(int Revision, string PackageSha256, string? OutcomeCode,
    string? FailureCode, string? RecommendationTitle, string? RecommendationDetail);

public sealed record GuidedModuleReviewResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc,
    string ReviewedManifestJson, GuidedThresholdDefinitionRequest Definition, IReadOnlyList<GuidedModuleReviewCaseResponse> Cases);

public sealed record GuidedModuleReviewCaseResponse(decimal? Value, string? OutcomeCode, string? FailureCode);

public sealed record ApproveGuidedModuleSubmissionRequest(string PackageSha256, string Reason);

public sealed record SubmitGuidedModuleDraftRequest(int ExpectedRevision, IReadOnlyList<decimal?> Readings);

public sealed record GuidedModuleSubmissionResponse(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc);
