using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Current-authority mutation fence. Requires an existing transaction and
/// holds the active user and direct Admin assignment until its write commits.
/// Replace this legacy predicate with paired grants at the complete cutover.
/// </summary>
internal static class LegacyAdministratorWriteGuard
{
    public static async Task RequireAsync(WellSiteAutoPilotDbContext db,
        SecurityActorContext actor, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Security mutation authorization requires a transaction.");
        if (actor.UserId is not { } userId || userId == Guid.Empty) throw Denied();
        try
        {
            // A snapshot-only check can serialize before a concurrently
            // committed revocation while its own write commits afterwards.
            // FOR SHARE blocks deletion of the authority row and user
            // deactivation until this authorized mutation finishes. If the
            // revocation wins first, reject or surface a conflict; never retry.
            var active = await db.Users.FromSqlInterpolated($"""
                SELECT u.* FROM security.users AS u
                INNER JOIN security.user_roles AS r ON r."UserId" = u."Id"
                WHERE u."Id" = {userId} AND u."IsActive" = TRUE AND r."Role" = 'Admin'
                FOR SHARE OF u, r
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (active.Length != 1) throw Denied();
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException("SECURITY_ADMIN_CONFLICT", FailureKind.Conflict,
                "Administrator authority changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private static WellSiteAutoPilotException Denied() => new("SECURITY_ADMIN_REQUIRED",
        FailureKind.Authorization, "An active persisted administrator is required for this mutation.");
}
