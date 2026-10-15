using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Transactional, explicit legacy grant materialization only. Does not change
/// legacy role tables, authorization middleware, token claims or cutover state.
/// A conflicting active entitlement aborts the ENTIRE deployment operation.
/// </summary>
public sealed class LegacyGrantMigrationRepository(WellSiteAutoPilotDbContext db)
    : ILegacyGrantMigrationRepository
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<LegacyGrantMigrationResult> MigrateAsync(
        Guid administratorId,
        string administratorIdentity,
        string? correlationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(administratorIdentity);
        if (administratorId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active administrator is required.");
        }
        occurredAtUtc = occurredAtUtc.ToUniversalTime();

        try
        {
            // Read/validate ALL accounts, role mappings, grants and Assets
            // under the SAME serializable snapshot used for the writes.
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var population = await new DeploymentMigrationSnapshotRepository(db)
                .CaptureInExistingTransactionAsync(
                    administratorId, cancellationToken);

            // Future Windows group SID verification does not exist yet.
            // Also reject grants to inactive users that could be reactivated.
            var unsupportedActiveGrants = await db.AccessGrants.AsNoTracking()
                .AnyAsync(grant => grant.IsActive &&
                    (grant.SubjectKind != "User" || grant.UserId == null ||
                     !db.Users.Any(user =>
                         user.Id == grant.UserId && user.IsActive)),
                    cancellationToken);
            if (unsupportedActiveGrants)
            {
                throw Conflict("Unverified group or inactive-user grants exist.");
            }

            var systemRoles = population.ActiveUsers.FirstOrDefault()?
                .Roles.Where(role => role.IsSystem).ToArray() ??
                Array.Empty<RoleDefinition>();
            var plannedByUser =
                new Dictionary<Guid, IReadOnlyCollection<AccessGrant>>();
            var usersAlreadyMatching = 0;

            foreach (var userSnapshot in population.ActiveUsers)
            {
                // Verify the currently recorded grant product against actual
                // legacy access for ALL known active Assets first.
                var parity = LegacyMigrationPreflightEvaluator.Evaluate(userSnapshot);
                if (parity.RecordedGrantsMatchLegacy)
                {
                    usersAlreadyMatching++;
                    continue;
                }

                if (userSnapshot.RecordedGrants.Any(grant => grant.IsActive))
                {
                    // Do not overwrite or union customer-authored draft grants:
                    // they may expand or reduce access when the future cutover
                    // runs. Require deliberate reconciliation instead.
                    throw Conflict(
                        "An active user's recorded grants differ from legacy access. Reconcile that user's grants before migration.");
                }

                var expected = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
                    userSnapshot.User, systemRoles,
                    userSnapshot.ActiveAssetIds);

                if (expected.Count == 0)
                {
                    // If a no-grant user has a mismatch, the snapshot or
                    // evaluator is inconsistent; never accept silent drift.
                    throw Conflict(
                        "Legacy access cannot be reproduced from the role catalog.");
                }

                var proposed = userSnapshot with
                {
                    RecordedGrants = expected
                };
                if (!LegacyMigrationPreflightEvaluator.Evaluate(proposed)
                    .RecordedGrantsMatchLegacy)
                {
                    throw Conflict(
                        "The migration proposal does not exactly match legacy permissions.");
                }
                plannedByUser.Add(userSnapshot.User.Id, expected);
            }

            var newGrants = plannedByUser.Values.SelectMany(items => items)
                .ToArray();
            // A retry after successful materialization is a no-op: it MUST
            // NOT write new grants, change revisions, or double-audit.
            if (newGrants.Length == 0)
            {
                await tx.CommitAsync(cancellationToken);
                return new LegacyGrantMigrationResult(
                    population.ActiveUsers.Count, population.ActiveAssetCount,
                    0, usersAlreadyMatching, true);
            }

            foreach (var grant in newGrants)
            {
                if (grant.UserId is not { } subject)
                {
                    throw Conflict("Only direct-user grants can be migrated.");
                }

                db.AccessGrants.Add(new AccessGrantEntity
                {
                    Id = grant.Id,
                    SubjectKind = "User",
                    UserId = subject,
                    RoleId = grant.RoleId,
                    ScopeKind = grant.ScopeKind.ToString(),
                    IsActive = true,
                    Revision = 1,
                    CreatedAtUtc = occurredAtUtc,
                    UpdatedAtUtc = occurredAtUtc
                });

                foreach (var assetId in grant.AssetIds)
                {
                    db.AccessGrantAssets.Add(new AccessGrantAssetEntity
                    {
                        GrantId = grant.Id,
                        AssetId = assetId
                    });
                }

                db.AuditEvents.Add(Audit(
                    administratorId, administratorIdentity,
                    correlationId, occurredAtUtc,
                    "security.grant.legacy-migrated",
                    grant.Id.ToString("D"),
                    new
                    {
                        grant.UserId, grant.RoleId,
                        scopeKind = grant.ScopeKind.ToString(),
                        assetIds = grant.AssetIds,
                        liveAuthorizationUnchanged = true
                    }));
            }

            db.AuditEvents.Add(Audit(
                administratorId, administratorIdentity,
                correlationId, occurredAtUtc,
                "security.legacy-grants.migration-recorded",
                administratorId.ToString("D"),
                new
                {
                    activeUsers = population.ActiveUsers.Count,
                    activeAssets = population.ActiveAssetCount,
                    created = newGrants.Length,
                    usersAlreadyMatching,
                    liveAuthorizationUnchanged = true
                }));

            // No authorization cutover flag, role mutation, or security token
            // changes are made here. All migration writes and audits commit
            // atomically, or all roll back.
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new LegacyGrantMigrationResult(
                population.ActiveUsers.Count, population.ActiveAssetCount,
                newGrants.Length, usersAlreadyMatching, true);
        }
        catch (PostgresException exception) when (
            exception.SqlState is
                PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected or
                PostgresErrorCodes.UniqueViolation)
        {
            throw Conflict(
                "A concurrent security change interrupted the migration. Review the current deployment and retry deliberately.",
                exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            throw Conflict(
                "Concurrent grant writes prevented an atomic migration.",
                exception);
        }
    }

    private static AuditEventEntity Audit(
        Guid administratorId, string identity, string? correlationId,
        DateTimeOffset time, string action, string targetId, object details) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = time,
            ActorUserId = administratorId,
            ActorIdentity = identity,
            Action = action,
            TargetType = "security-access-grant",
            TargetId = targetId,
            CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(details, JsonOptions)
        };

    private static WellSiteAutoPilotException Conflict(
        string message, Exception? cause = null) =>
        new("SECURITY_MIGRATION_CONFLICT", FailureKind.Conflict, message,
            innerException: cause);
}
