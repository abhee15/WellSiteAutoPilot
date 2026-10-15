namespace WellSiteAutoPilot.Application.Security;

/// <summary>Persist a privileged diagnostic before its result is disclosed.</summary>
public interface IScopedAccessInspectionAuditRepository
{
    Task RecordAsync(
        ScopedAccessInspection inspection,
        SecurityActorContext actor,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
