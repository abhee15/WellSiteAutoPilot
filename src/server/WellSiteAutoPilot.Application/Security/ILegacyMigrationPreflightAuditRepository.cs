namespace WellSiteAutoPilot.Application.Security;

public interface ILegacyMigrationPreflightAuditRepository
{
    Task RecordUserAsync(LegacyMigrationPreflightResult result, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task RecordDeploymentAsync(DeploymentMigrationPreflightResult result, SecurityActorContext actor,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
