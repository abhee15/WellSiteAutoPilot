using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Audit-only grant materialization. This operation never switches runtime
/// authorization away from the legacy role and Asset scope model.
/// </summary>
public sealed record LegacyGrantMigrationResult(
    int ActiveUsersInspected,
    int ActiveAssetsInspected,
    int GrantsCreated,
    int UsersAlreadyMatching,
    bool RecordedGrantsMatchLegacy,
    bool LiveAuthorizationUnchanged = true);

public interface ILegacyGrantMigrationRepository
{
    Task<LegacyGrantMigrationResult> MigrateAsync(
        Guid administratorId,
        string administratorIdentity,
        string? correlationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// No implicit migration on startup or login. Explicit, separately confirmed
/// administrator action only; persistence handles the atomic snapshot,
/// conflicts, auditing, idempotency and final equivalence verification.
/// </summary>
public sealed class LegacyGrantMigrationService(
    AccessGrantAdministrationService administration,
    ILegacyGrantMigrationRepository migration,
    TimeProvider timeProvider)
{
    public async Task<LegacyGrantMigrationResult> MigrateAsync(
        bool confirmed,
        SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_CONFIRMATION_REQUIRED",
                FailureKind.Validation,
                "Explicit confirmation is required to materialize legacy grants.");
        }

        await administration.RequireAdministratorAsync(actor, cancellationToken);
        if (actor.UserId is not { } administratorId ||
            string.IsNullOrWhiteSpace(actor.IdentityName))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active administrator is required for migration.");
        }

        return await migration.MigrateAsync(
            administratorId, actor.IdentityName, actor.CorrelationId,
            timeProvider.GetUtcNow(), cancellationToken);
    }
}
