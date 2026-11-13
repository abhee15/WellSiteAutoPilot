using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

public sealed class GuidedModuleDraftService(IGuidedModuleDraftRepository drafts, TimeProvider clock,
    LogicModulePackageService packages, LogicModuleApprovalService approvals)
{
    public Task<IReadOnlyList<GuidedModuleDraft>> ListAsync(AccessSubject subject, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_PAGE_INVALID", FailureKind.Validation,
                "Provide a nonnegative offset and a page size from 1 to 100.");
        return drafts.ListOwnedAsync(subject, offset, limit, cancellationToken);
    }

    public async Task<GuidedModuleDraft> CreateAsync(GuidedModuleDraftContent content, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await DeclarativeThresholdPackageBuilder.BuildAsync(content.Manifest, content.Definition, cancellationToken);
        var draft = new GuidedModuleDraft(Guid.NewGuid(), subject.UserId, 1, content);
        await drafts.AddOwnedAsync(draft, subject, actor, cancellationToken);
        return draft;
    }

    public async Task<DeclarativeThresholdEvidence> PreviewAsync(Guid id, int expectedRevision, decimal? value,
        string parametersJson, AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var draft = await GetAsync(id, subject, cancellationToken);
        var submission = await draft.PrepareSubmissionAsync(expectedRevision,
            [CreateSample(draft, value, parametersJson)], cancellationToken);
        return submission.Evidence.Single();
    }

    public async Task<GuidedModuleReviewSnapshot> GetSubmissionAsync(Guid id, int revision, AccessSubject subject,
        CancellationToken cancellationToken = default) =>
        await drafts.FindSubmissionAsync(id, revision, subject, cancellationToken) ??
        throw new WellSiteAutoPilotException("GUIDED_SUBMISSION_NOT_FOUND", FailureKind.NotFound, "The guided submission was not found.");

    public async Task<InstalledLogicModule> ApproveSubmissionAsync(Guid id, int revision, string expectedPackageSha256,
        string reason, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        var reviewed = await GetSubmissionAsync(id, revision, subject, cancellationToken);
        if (!string.Equals(expectedPackageSha256, reviewed.PackageSha256, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("GUIDED_SUBMISSION_HASH_MISMATCH", FailureKind.Conflict,
                "The submitted package differs from the reviewed package.");
        var reviewedReason = LogicModuleApproval.ValidateReason(reason);

        // Registration and approval can be retried independently. Registration pins
        // the exact bytes and leaves a new package untrusted until approval commits.
        using var bytes = new MemoryStream(reviewed.PackageBytes.ToArray(), writable: false);
        var registered = await packages.UploadAsync(bytes, subject, actor, cancellationToken);
        return await approvals.ApproveAsync(registered.ModuleId, registered.Version, reviewedReason,
            subject, actor, cancellationToken);
    }

    public async Task<GuidedModuleSubmissionReceipt> SubmitAsync(Guid id, int expectedRevision,
        IReadOnlyList<decimal?> readings, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (readings is null || readings.Count is < 1 or > 50)
            throw new WellSiteAutoPilotException("GUIDED_DRAFT_SCENARIOS_INVALID", FailureKind.Validation,
                "Provide between one and fifty sample readings.");
        var draft = await GetAsync(id, subject, cancellationToken);
        var samples = readings.Select(value => CreateSample(draft, value, "{}")).ToArray();
        var submission = await draft.PrepareSubmissionAsync(expectedRevision, samples, cancellationToken);
        return await drafts.SubmitOwnedAsync(draft, submission, subject, actor, cancellationToken);
    }

    private DeclarativeThresholdSample CreateSample(GuidedModuleDraft draft, decimal? value, string parametersJson)
    {
        var asset = Guid.NewGuid();
        var run = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var requirement = draft.Content.Manifest.DataRequirements.Single();
        LogicInputValue? input = value is { } reading
            ? new(requirement.Id, asset, requirement.Quantity, reading, requirement.CanonicalUnit!, now, "Good", "guided-preview")
            : null;
        return new(run, run.ToString("D"), asset, now, parametersJson, "{}", input);
    }

    public async Task<GuidedModuleDraft> GetAsync(Guid id, AccessSubject subject, CancellationToken cancellationToken = default) =>
        await drafts.FindOwnedAsync(id, subject, cancellationToken) ??
        throw new WellSiteAutoPilotException("GUIDED_DRAFT_NOT_FOUND", FailureKind.NotFound, "The guided draft was not found.");

    public async Task<GuidedModuleDraft> EditAsync(Guid id, int expectedRevision, GuidedModuleDraftContent content,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        var draft = await GetAsync(id, subject, cancellationToken);
        var edited = draft.Edit(expectedRevision, content);
        await DeclarativeThresholdPackageBuilder.BuildAsync(content.Manifest, content.Definition, cancellationToken);
        await drafts.UpdateOwnedAsync(edited, expectedRevision, subject, actor, cancellationToken);
        return edited;
    }
}
