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

    /// <summary>Records only that a discovery was run, with counts, so the audit trail shows who looked and what they saw.</summary>
    Task RecordDiscoveryAuthorizedAsync(Guid id, int found, int warnings, bool complete, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    Task DeleteAuthorizedAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default);
}
