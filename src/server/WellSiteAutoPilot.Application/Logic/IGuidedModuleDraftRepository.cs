using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Logic;

public interface IGuidedModuleDraftRepository
{
    Task<GuidedModuleDraft?> FindOwnedAsync(Guid id, AccessSubject subject, CancellationToken cancellationToken = default);
    Task AddOwnedAsync(GuidedModuleDraft draft, AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);
    Task UpdateOwnedAsync(GuidedModuleDraft draft, int expectedRevision, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);
}
