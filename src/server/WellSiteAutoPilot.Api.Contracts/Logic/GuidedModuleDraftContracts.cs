namespace WellSiteAutoPilot.Api.Contracts.Logic;

public sealed record GuidedThresholdDefinitionRequest(string Parameter, decimal Default, string OutcomeCode, string Title, string Detail);

public sealed record SaveGuidedModuleDraftRequest(string ManifestJson, GuidedThresholdDefinitionRequest Definition, int? ExpectedRevision);

public sealed record GuidedModuleDraftResponse(Guid Id, int Revision, string ManifestJson, GuidedThresholdDefinitionRequest Definition);

public sealed record PreviewGuidedModuleDraftRequest(int ExpectedRevision, decimal? Value, string ParametersJson);

public sealed record GuidedModulePreviewResponse(int Revision, string PackageSha256, string? OutcomeCode,
    string? FailureCode, string? RecommendationTitle, string? RecommendationDetail);
