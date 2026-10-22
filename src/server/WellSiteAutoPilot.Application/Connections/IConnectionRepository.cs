using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Connections;

public interface IConnectionRepository
{
    Task<IReadOnlyList<ConnectionRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task<ConnectionRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task CreateAuthorizedAsync(ConnectionRecord connection, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refused as stale when <paramref name="expectedRevision"/> is not the stored revision. Once Assets were imported
    /// from the connection, changing any of <paramref name="sourceSettingKeys"/> (the settings that say which system it
    /// reads) is refused, because the stored external identities only mean something in that system.
    /// </summary>
    Task<ConnectionRecord> UpdateAuthorizedAsync(Guid id, int expectedRevision, string name, int settingsVersion,
        IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> sourceSettingKeys, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task<ConnectionRecord> SetEnabledAuthorizedAsync(Guid id, int expectedRevision, bool enabled, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>Keeps the result only when it was produced for the stored revision; otherwise the stored record is returned unchanged.</summary>
    Task<ConnectionRecord> RecordTestAuthorizedAsync(Guid id, ConnectionTestRecord result, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes up to <paramref name="limit"/> enabled connections whose next check is due, and leases each for
    /// <paramref name="lease"/> so no other Server instance checks the same one meanwhile. Returns their ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> ClaimDueChecksAsync(DateTimeOffset now, TimeSpan lease, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a scheduled check as the monitor, which holds no grants. Kept only when the connection is still enabled
    /// and still at the tested revision. <paramref name="delayAfter"/> maps the new count of failures in a row (0 after
    /// a success) to the wait before the next check. An audit event is written only when the outcome changed.
    /// </summary>
    Task<ScheduledCheckOutcome> RecordScheduledCheckAsync(Guid id, ConnectionTestRecord result,
        Func<int, TimeSpan> delayAfter, CancellationToken cancellationToken = default);

    /// <summary>Records only that a discovery was run, with counts, so the audit trail shows who looked and what they saw.</summary>
    Task RecordDiscoveryAuthorizedAsync(Guid id, int found, int warnings, bool complete, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task DeleteAuthorizedAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);
}

/// <summary>What recording a scheduled check did. <see cref="Changed"/> means a person looking would now see something different.</summary>
public sealed record ScheduledCheckOutcome(bool Recorded, bool Changed);
