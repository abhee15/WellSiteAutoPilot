using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <param name="ExpectedRevisionId">The active revision the caller reviewed.</param>
/// <param name="ExpectedVersion">The state version the caller reviewed; 0 when the logic still follows its revision.</param>
/// <param name="Schedule">The complete schedule that becomes live. The caller derives it; the repository only stores it.</param>
public sealed record ConfiguredLogicScheduleChangeRequest(
    Guid ConfiguredLogicId,
    Guid ExpectedRevisionId,
    int ExpectedVersion,
    ConfiguredLogicScheduleChangeKind Kind,
    ConfiguredLogicSchedule Schedule,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason);

public interface IConfiguredLogicScheduleRepository
{
    Task<ConfiguredLogicScheduleState?> GetAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default);

    /// <summary>Live state for each id that has one; logic that follows its revision is absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, ConfiguredLogicScheduleState>> ListAsync(
        IReadOnlyCollection<Guid> configuredLogicIds,
        CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ConfiguredLogicScheduleChange>> ListChangesAsync(
        Guid configuredLogicId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies one change atomically with respect to revision activation and to other changes. Throws a conflict when the
    /// active revision or the state version differs from what the caller reviewed.
    /// </summary>
    Task<ConfiguredLogicScheduleState> ApplyAsync(
        ConfiguredLogicScheduleChangeRequest request,
        CancellationToken cancellationToken = default);
}
