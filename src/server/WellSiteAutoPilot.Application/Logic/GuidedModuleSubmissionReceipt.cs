namespace WellSiteAutoPilot.Application.Logic;

public sealed record GuidedModuleSubmissionReceipt(Guid DraftId, int Revision, string PackageSha256,
    DateTimeOffset SubmittedAtUtc);

public sealed record GuidedModuleReviewSnapshot(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc,
    ReadOnlyMemory<byte> PackageBytes, string ReviewedManifestJson, ThresholdDefinition Definition, IReadOnlyList<DeclarativeThresholdEvidence> Evidence);
