using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Saved authoring content. <see cref="Definition"/> is the only executable logic Guided Authoring supports today.
/// When it is null the draft is a portable typed contract (roles, inputs, outputs, parameters) with no logic yet:
/// it can be saved, reopened and inspected, but never previewed or submitted.
/// </summary>
public sealed record GuidedModuleDraftContent(LogicModuleManifest Manifest, ThresholdDefinition? Definition = null,
    IReadOnlyList<GuidedSampleCase>? SampleCases = null, IReadOnlyList<GuidedSourceHint>? SourceHints = null,
    string? RulesJson = null)
{
    public bool IsContractOnly => Definition is null;
}

/// <summary>Editable authoring state. Submission captures an artifact from an exact draft revision.</summary>
public sealed record GuidedModuleDraft(
    Guid Id, Guid OwnerUserId, int Revision, GuidedModuleDraftContent Content)
{
    public GuidedModuleDraft Edit(int expectedRevision, GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        RequireRevision(expectedRevision);
        if (Content.IsContractOnly != content.IsContractOnly)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_KIND_IMMUTABLE", FailureKind.Validation,
                "A guided draft cannot switch between a contract-only draft and a threshold rule.");
        if (!string.Equals(Content.Manifest.RequiredAdapterKey, content.Manifest.RequiredAdapterKey, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_PROVIDER_IMMUTABLE", FailureKind.Validation,
                "A guided draft keeps the provider it was created for. Copy the contract into a new draft to target another provider.");
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
        RequireThresholdLogic();
        if (cases.Count is < 1 or > 50)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_SCENARIOS_INVALID", FailureKind.Validation,
                "Provide between one and fifty scenarios for the submitted draft.");

        var artifact = await DeclarativeThresholdPackageBuilder.BuildAsync(Content.Manifest, Content.Definition!, cancellationToken);
        var evidence = new List<DeclarativeThresholdEvidence>(cases.Count);
        foreach (var sample in cases)
            evidence.Add(await DeclarativeThresholdPreview.EvaluateAsync(artifact, sample, cancellationToken));

        return new(Id, Revision, artifact, evidence.AsReadOnly());
    }

    /// <summary>Contract-only drafts have no supported runtime path, so preview and submission fail explicitly.</summary>
    internal void RequireThresholdLogic()
    {
        if (Content.IsContractOnly)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_LOGIC_UNSUPPORTED", FailureKind.Validation,
                "This draft declares a contract but has no supported logic to preview or submit yet.");
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
