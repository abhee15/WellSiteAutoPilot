using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public interface IConfiguredLogicRepository
{
    Task SetRevisionValidatedAuthorizedAsync(ConfiguredLogicRevision reviewedRevision, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task ActivateRevisionAuthorizedAsync(ConfiguredLogicRevision reviewedRevision, Guid? expectedActiveRevisionId,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a live schedule change under the same grant, scope and audit rules as revision activation. The actor is
    /// recorded as the author; the request's own author is ignored.
    /// </summary>
    Task<ConfiguredLogicScheduleState> ApplyScheduleChangeAuthorizedAsync(ConfiguredLogicScheduleChangeRequest request,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task AddAuthorizedAsync(ConfiguredLogicDefinition definition, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task AddRevisionAuthorizedAsync(ConfiguredLogicRevision revision, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task<ConfiguredLogicDefinition?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAuthorizedAsync(AccessSubject subject, int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ConfiguredLogicDefinition configuredLogic,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default);

    Task AddRevisionAsync(
        ConfiguredLogicRevision revision,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default);

    Task<ConfiguredLogicDefinition?> GetAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default);

    Task<ConfiguredLogicDefinition?> GetInScopeAsync(
        Guid configuredLogicId,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListInScopeAsync(
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListActiveScheduledAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task SetRevisionValidatedAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset validatedAtUtc,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default);

    Task ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset activatedAtUtc,
        Guid? expectedActiveRevisionId,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default);
}
