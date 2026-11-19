namespace WellSiteAutoPilot.Application.Logic;

public sealed record GuidedModuleSubmissionReceipt(Guid DraftId, int Revision, string PackageSha256,
    DateTimeOffset SubmittedAtUtc);

public sealed record GuidedModuleReviewItem(Guid DraftId, int Revision, string DisplayName, DateTimeOffset SubmittedAtUtc);

public sealed record GuidedModuleReviewSnapshot(Guid DraftId, int Revision, string PackageSha256, DateTimeOffset SubmittedAtUtc,
    ReadOnlyMemory<byte> PackageBytes, string ReviewedManifestJson, ThresholdDefinition? Definition, IReadOnlyList<DeclarativeThresholdEvidence> Evidence, bool IsPressureDifference)
{
    /// <summary>Set for rules modules: the reviewed definition and the author's scenario results, as stored.</summary>
    public string? RulesJson { get; init; }
    public string? RulesEvidenceJson { get; init; }
}
