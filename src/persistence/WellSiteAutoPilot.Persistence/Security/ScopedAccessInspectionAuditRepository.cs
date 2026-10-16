using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class ScopedAccessInspectionAuditRepository(WellSiteAutoPilotDbContext db)
    : IScopedAccessInspectionAuditRepository
{
    public Task RecordAsync(ScopedAccessInspection inspection, SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return SecurityDiagnosticAuditWriter.RecordAsync(db, actor, nowUtc,
            "security.access.inspected", "security-user", inspection.UserId.ToString("D"), inspection.AssetId,
            new
            {
                permissionCodes = inspection.Permissions.Select(item => item.Permission).ToArray(),
                inspection.WideningCount, inspection.ReductionCount,
                diagnosticOnly = true, liveAuthorizationUnchanged = true
            }, cancellationToken);
    }
}
