using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class LegacyMigrationPreflightAuditRepository(WellSiteAutoPilotDbContext db)
    : ILegacyMigrationPreflightAuditRepository
{
    public Task RecordUserAsync(LegacyMigrationPreflightResult result, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return SecurityDiagnosticAuditWriter.RecordAsync(db, actor, nowUtc,
            "security.migration.user-preflight-inspected", "security-user", result.UserId.ToString("D"), null,
            new
            {
                result.ActiveAssetCount, result.ExpectedLegacyGrantCount, result.RecordedGrantCount,
                result.ExpansionCount, result.ReductionCount, result.RecordedGrantsMatchLegacy,
                diagnosticOnly = true, liveAuthorizationUnchanged = true
            }, cancellationToken);
    }

    public Task RecordDeploymentAsync(DeploymentMigrationPreflightResult result, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return SecurityDiagnosticAuditWriter.RecordAsync(db, actor, nowUtc,
            "security.migration.deployment-preflight-inspected", "security-deployment", "deployment", null,
            new
            {
                result.ActiveUserCount, result.ActiveAssetCount, result.MatchingUserCount,
                result.ExpansionCount, result.ReductionCount, result.AllActiveUsersMatchRecordedGrants,
                diagnosticOnly = true, liveAuthorizationUnchanged = true
            }, cancellationToken);
    }
}
