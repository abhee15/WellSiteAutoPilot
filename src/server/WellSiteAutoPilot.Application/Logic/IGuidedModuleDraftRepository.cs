using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Logic;

public interface IGuidedModuleDraftRepository
{
    Task<IReadOnlyList<GuidedModuleDraft>> ListOwnedAsync(AccessSubject subject, int offset, int limit, CancellationToken cancellationToken = default);
    Task<GuidedModuleDraft?> FindOwnedAsync(Guid id, AccessSubject subject, CancellationToken cancellationToken = default);
    Task AddOwnedAsync(GuidedModuleDraft draft, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);
    Task UpdateOwnedAsync(GuidedModuleDraft draft, int expectedRevision, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);
    Task<GuidedModuleReviewSnapshot?> FindSubmissionAsync(Guid id, int revision, AccessSubject subject, CancellationToken cancellationToken = default);
    Task<GuidedModuleSubmissionReceipt> SubmitOwnedAsync(GuidedModuleDraft draft, GuidedModuleSubmission submission,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);
}
