using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed class LegacyMigrationPreflightService(
    AccessGrantAdministrationService administration,
    ILegacyMigrationSnapshotRepository snapshots,
    ILegacyMigrationPreflightAuditRepository audit,
    TimeProvider clock)
{
    public async Task<LegacyMigrationPreflightResult> InspectAsync(
        Guid userId, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await administration.RequireAdministratorAsync(actor, cancellationToken);
        if (userId == Guid.Empty || actor.UserId is not { } administratorId)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_PREFLIGHT_INVALID",
                FailureKind.Validation, "A valid user and administrator are required.");
        }

        // Snapshot repository revalidates the actor inside its transaction.
        var snapshot = await snapshots.CaptureAsync(
            userId, administratorId, cancellationToken);

        var result = LegacyMigrationPreflightEvaluator.Evaluate(snapshot);
        await audit.RecordUserAsync(result, actor, clock.GetUtcNow(), cancellationToken);
        return result;
    }
}
