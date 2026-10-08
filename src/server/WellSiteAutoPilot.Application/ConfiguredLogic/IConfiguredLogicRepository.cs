using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public interface IConfiguredLogicRepository
{
    Task AddAsync(
        ConfiguredLogicDefinition configuredLogic,
        CancellationToken cancellationToken = default);

    Task AddRevisionAsync(
        ConfiguredLogicRevision revision,
        CancellationToken cancellationToken = default);

    Task<ConfiguredLogicDefinition?> GetAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListActiveScheduledAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task SetRevisionValidatedAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken = default);

    Task ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken = default);
}
