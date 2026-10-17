using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

internal static class GrantAdministrationActor
{
    public static SecurityActorContext Bind(AccessSubject subject, SecurityActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (subject.UserId == Guid.Empty || actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) ||
            actor.IdentityName.Length > 256 || actor.IdentityName.Any(char.IsControl) ||
            (actor.CorrelationId is { } correlation && (string.IsNullOrWhiteSpace(correlation) || correlation.Length > 128 || correlation.Any(char.IsControl))))
            throw new WellSiteAutoPilotException("SECURITY_ACTOR_REQUIRED", FailureKind.Authorization,
                "A bound authenticated administrator is required.");
        // Own-group protections must consume the same trusted observation as
        // administrator authority, never an independently supplied actor claim.
        return actor with { WindowsGroups = subject.WindowsGroups };
    }

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException("SECURITY_ADMIN_CONFLICT", FailureKind.Conflict,
                "Administrator authority or its target changed concurrently. Reload before retrying.", innerException: exception);
        }
    }
}
