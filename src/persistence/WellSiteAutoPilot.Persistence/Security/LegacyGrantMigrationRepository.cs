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
    public const int MaximumProvenanceRecords = 600;
    public const int MaximumUserGrantRecords = 500;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Atomic rollback of ALL still-active grants originally created by a
    /// successful legacy migration. Provenance comes from the server-written
    /// migration audit event, not from a caller-provided list of grant IDs.
    /// Never deletes audit history and never edits compiled legacy RBAC.
    /// </summary>
    public async Task<LegacyGrantRollbackResult> RollbackAsync(
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
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var authorized = await db.Users.AsNoTracking().AnyAsync(
                user => user.Id == administratorId && user.IsActive,
                cancellationToken) &&
                await db.UserRoles.AsNoTracking().AnyAsync(
                    role => role.UserId == administratorId && role.Role == "Admin",
                    cancellationToken);
            if (!authorized)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                    "A currently active administrator is required for rollback.");
            }

            // Every migrated assignment has a server-written audit record
            // in the same transaction that created the assignment. Refuse
            // any incomplete/oversize set rather than partially rolling back.
            var provenance = await db.AuditEvents.AsNoTracking()
                .Where(item => item.Action == "security.grant.legacy-migrated" &&
                    item.TargetType == "security-access-grant")
                .Select(item => new { item.TargetId, item.DetailsJson })
                .Take(MaximumProvenanceRecords + 1)
                .ToArrayAsync(cancellationToken);
            var auditTargets = provenance.Select(item => item.TargetId).ToArray();
            if (auditTargets.Length > MaximumProvenanceRecords ||
                auditTargets.Any(value => !Guid.TryParse(value, out var id) ||
                    id == Guid.Empty))
            {
                throw Conflict(
                    "Migration provenance is incomplete or exceeds safe rollback limits.");
            }

            var migrationIds = auditTargets.Select(value => Guid.Parse(value!))
                .Distinct().ToArray();
            if (migrationIds.Length != auditTargets.Length)
            {
                throw Conflict("Duplicate migration provenance requires manual review.");
            }

            var migratedRows = await db.AccessGrants.AsNoTracking()
                .Where(grant => migrationIds.Contains(grant.Id))
                .OrderBy(grant => grant.Id)
                .ToArrayAsync(cancellationToken);
            if (migratedRows.Length != migrationIds.Length ||
                migratedRows.Any(grant =>
                    grant.SubjectKind != "User" || grant.UserId is null ||
                    grant.GroupId is not null ||
                    grant.RoleId == Guid.Empty || grant.Revision < 1 ||
                    (grant.IsActive && grant.Revision != 1)))
            {
                throw Conflict(
                    "A migrated assignment is missing, modified or malformed. No rollback performed.");
            }

            var bindings = await db.AccessGrantAssets.AsNoTracking()
                .Where(item => migrationIds.Contains(item.GrantId))
                .ToArrayAsync(cancellationToken);
            var proofById = provenance.ToDictionary(item => Guid.Parse(item.TargetId!), item => item.DetailsJson);
            if (migratedRows.Any(grant => !MatchesProvenance(grant,
                bindings.Where(binding => binding.GrantId == grant.Id).Select(binding => binding.AssetId).ToArray(),
                proofById[grant.Id])))
            {
                throw Conflict("Migration assignment differs from its immutable provenance. No rollback performed.");
            }

            var active = migratedRows.Where(grant => grant.IsActive).ToArray();
            if (active.Length == 0)
            {
                await tx.CommitAsync(cancellationToken);
                return new LegacyGrantRollbackResult(
                    migrationIds.Length, 0);
            }

            var activeIds = active.Select(grant => grant.Id).ToArray();
            var changed = await db.AccessGrants
                .Where(grant => activeIds.Contains(grant.Id) &&
                    grant.SubjectKind == "User" && grant.IsActive &&
                    grant.Revision == 1)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(grant => grant.IsActive, false)
                    .SetProperty(grant => grant.Revision, 2L)
                    .SetProperty(grant => grant.UpdatedAtUtc, occurredAtUtc),
                    cancellationToken);
            if (changed != active.Length)
            {
                throw Conflict(
                    "A concurrent change prevented all-or-nothing rollback.");
            }

            foreach (var grant in active)
            {
                db.AuditEvents.Add(Audit(
                    administratorId, administratorIdentity,
                    correlationId, occurredAtUtc,
                    "security.grant.legacy-rolled-back",
                    grant.Id.ToString("D"),
                    new
                    {
                        grant.UserId, grant.RoleId, grant.ScopeKind,
                        previousRevision = 1, newRevision = 2,
                        liveAuthorizationUnchanged = true
                    }));
            }
            db.AuditEvents.Add(Audit(
                administratorId, administratorIdentity,
                correlationId, occurredAtUtc,
                "security.legacy-grants.rollback-recorded",
                administratorId.ToString("D"),
                new
                {
                    migratedGrantsInspected = migrationIds.Length,
                    grantsDeactivated = active.Length,
                    liveAuthorizationUnchanged = true
                }));

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new LegacyGrantRollbackResult(
                migrationIds.Length, active.Length);
        }
        catch (PostgresException exception) when (
            exception.SqlState is
                PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected)
        {
            throw Conflict(
                "Concurrent security changes interrupted rollback. Inspect the current grants before retrying.",
                exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
            })
        {
            throw Conflict("Concurrent security changes interrupted rollback. Inspect current grants before retrying.", exception);
        }
        catch (InvalidOperationException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is
                PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected)
        {
            throw Conflict(
                "Concurrent security changes interrupted rollback. Inspect the current grants before retrying.",
                exception);
        }
    }

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
                if (userSnapshot.RecordedGrants.Count + expected.Count > MaximumUserGrantRecords)
                {
                    throw Conflict("Migration would exceed the complete user grant inspection limit.");
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

            // Rollback consumes retained, immutable provenance, including
            // earlier rolled-back runs. Never create an unrollable batch.
            var retainedProvenance = await db.AuditEvents.AsNoTracking()
                .Where(item => item.Action == "security.grant.legacy-migrated" &&
                    item.TargetType == "security-access-grant")
                .Take(MaximumProvenanceRecords + 1).CountAsync(cancellationToken);
            if (retainedProvenance + newGrants.Length > MaximumProvenanceRecords)
            {
                throw Conflict("Migration would exceed the complete rollback provenance limit.");
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
                SqlState: PostgresErrorCodes.UniqueViolation or
                    PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
            })
        {
            throw Conflict(
                "Concurrent grant writes prevented an atomic migration.",
                exception);
        }
        catch (InvalidOperationException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is
                PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected)
        {
            throw Conflict(
                "Concurrent security changes prevented an atomic migration.",
                exception);
        }
    }

    private static bool MatchesProvenance(AccessGrantEntity grant, Guid[] assets, string details)
    {
        try
        {
            using var document = JsonDocument.Parse(details);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("userId", out var user) || user.ValueKind != JsonValueKind.String ||
                !user.TryGetGuid(out var userId) || userId != grant.UserId ||
                !root.TryGetProperty("roleId", out var role) || role.ValueKind != JsonValueKind.String ||
                !role.TryGetGuid(out var roleId) || roleId != grant.RoleId ||
                !root.TryGetProperty("scopeKind", out var scope) || scope.ValueKind != JsonValueKind.String ||
                scope.GetString() != grant.ScopeKind ||
                !root.TryGetProperty("assetIds", out var recorded) || recorded.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var ids = new List<Guid>();
            foreach (var item in recorded.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !item.TryGetGuid(out var id) || id == Guid.Empty)
                    return false;
                ids.Add(id);
            }
            return ids.Count == ids.Distinct().Count() &&
                ids.OrderBy(id => id).SequenceEqual(assets.OrderBy(id => id));
        }
        catch (JsonException)
        {
            return false;
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
