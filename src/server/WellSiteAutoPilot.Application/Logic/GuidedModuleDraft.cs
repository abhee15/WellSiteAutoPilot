using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public sealed record GuidedModuleDraftContent(LogicModuleManifest Manifest, ThresholdDefinition Definition);

/// <summary>Editable authoring state. Submission captures an artifact from an exact draft revision.</summary>
public sealed record GuidedModuleDraft(
    Guid Id, Guid OwnerUserId, int Revision, GuidedModuleDraftContent Content)
{
    public GuidedModuleDraft Edit(int expectedRevision, GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        RequireRevision(expectedRevision);
        if (!string.Equals(Content.Manifest.ModuleId, content.Manifest.ModuleId, StringComparison.Ordinal) ||
            !string.Equals(Content.Manifest.Version, content.Manifest.Version, StringComparison.Ordinal) ||
            Content.Manifest.EntryPoint != content.Manifest.EntryPoint)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_IDENTITY_IMMUTABLE", FailureKind.Validation,
                "A guided draft edit must preserve the module identity, version and calculation.");
        return this with { Content = content, Revision = checked(Revision + 1) };
    }

    public async Task<GuidedModuleSubmission> PrepareSubmissionAsync(
        int expectedRevision, IReadOnlyList<DeclarativeThresholdSample> cases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        RequireRevision(expectedRevision);
        if (cases.Count is < 1 or > 50)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_SCENARIOS_INVALID", FailureKind.Validation,
                "Provide between one and fifty scenarios for the submitted draft.");

        var artifact = await DeclarativeThresholdPackageBuilder.BuildAsync(Content.Manifest, Content.Definition, cancellationToken);
        var evidence = new List<DeclarativeThresholdEvidence>(cases.Count);
        foreach (var sample in cases)
            evidence.Add(await DeclarativeThresholdPreview.EvaluateAsync(artifact, sample, cancellationToken));

        return new(Id, Revision, artifact, evidence.AsReadOnly());
    }

    internal void RequireRevision(int expectedRevision)
    {
        if (expectedRevision != Revision)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_REVISION_CONFLICT", FailureKind.Conflict,
                "The guided draft changed. Reload it before retrying.");
    }
}

public sealed record GuidedModuleSubmission(
    Guid DraftId, int DraftRevision, DeclarativeThresholdArtifact Artifact,
    IReadOnlyList<DeclarativeThresholdEvidence> Evidence);
