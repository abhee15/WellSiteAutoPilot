using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

public sealed class GuidedModuleDraftService(IGuidedModuleDraftRepository drafts, TimeProvider clock)
{
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
        var asset = Guid.NewGuid();
        var run = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var requirement = draft.Content.Manifest.DataRequirements.Single();
        LogicInputValue? input = value is { } reading
            ? new(requirement.Id, asset, requirement.Quantity, reading, requirement.CanonicalUnit!, now, "Good", "guided-preview")
            : null;
        var submission = await draft.PrepareSubmissionAsync(expectedRevision,
            [new(run, run.ToString("D"), asset, now, parametersJson, "{}", input)], cancellationToken);
        return submission.Evidence.Single();
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
