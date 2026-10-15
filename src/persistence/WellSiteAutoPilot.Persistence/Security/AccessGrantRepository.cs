using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>Explicit direct-user entitlements, currently audit-only until cutover.</summary>
public sealed class AccessGrantRepository(WellSiteAutoPilotDbContext dbContext)
    : IAccessGrantRepository
{
    private static readonly JsonSerializerOptions AuditOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<AccessGrant>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.AccessGrants.AsNoTracking()
            .Where(item => item.SubjectKind == "User" && item.UserId == userId)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Take(501)
            .ToArrayAsync(cancellationToken);

        // Never silently truncate entitlements: the inspector and UI must
        // not claim a partial set represents the user's access.
        if (rows.Length > 500)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_LIST_TOO_LARGE", FailureKind.Validation,
                "The user has too many assignment records for a complete inspection.");
        }

        if (rows.Length == 0)
        {
            return [];
        }

        var ids = rows.Select(item => item.Id).ToArray();
        var bindings = await dbContext.AccessGrantAssets.AsNoTracking()
            .Where(item => ids.Contains(item.GrantId))
            .OrderBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return rows.Select(item => ToDomain(item,
            bindings.Where(binding => binding.GrantId == item.Id)
                .Select(binding => binding.AssetId))).ToArray();
    }

    public async Task<AccessGrant> CreateForUserAsync(
        Guid userId, CreateAccessGrantCommand command,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        nowUtc = nowUtc.ToUniversalTime();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive,
                cancellationToken);
        var admin = await dbContext.Users.AsNoTracking()
            .AnyAsync(item => item.Id == actor.UserId && item.IsActive,
                cancellationToken);
        var isAdmin = await dbContext.UserRoles.AsNoTracking()
            .AnyAsync(item => item.UserId == actor.UserId && item.Role == "Admin",
                cancellationToken);
        var role = await dbContext.RoleDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.RoleId && item.IsActive,
                cancellationToken);

        if (user is null || !admin || !isAdmin || role is null ||
            actor.UserId == userId)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_FORBIDDEN",
                FailureKind.Authorization,
                "Grant assignment requires an active user, role, and distinct administrator.");
        }

        var scopeIds = command.AssetIds.Distinct().ToArray();
        if (scopeIds.Length != command.AssetIds.Count ||
            (command.ScopeKind == AccessGrantScopeKind.ExactAssets &&
             scopeIds.Length == 0) ||
            (command.ScopeKind != AccessGrantScopeKind.ExactAssets &&
             scopeIds.Length != 0) ||
            scopeIds.Length > 500 ||
            scopeIds.Any(item => item == Guid.Empty))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_SCOPE_INVALID",
                FailureKind.Validation,
                "Invalid grant Asset scope.");
        }

        if (scopeIds.Length > 0)
        {
            var count = await dbContext.Assets.AsNoTracking()
                .CountAsync(item => scopeIds.Contains(item.Id) && item.IsActive,
                    cancellationToken);
            if (count != scopeIds.Length)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_GRANT_ASSET_INACTIVE",
                    FailureKind.Validation,
                    "All granted Assets must exist and be active.");
            }
        }

        // Recheck role permission/scope compatibility transactionally. This
        // protects against concurrent role deactivation or content changes.
        var scopes = await dbContext.RolePermissions.AsNoTracking()
            .Where(item => item.RoleId == command.RoleId)
            .Select(item => item.Permission)
            .ToArrayAsync(cancellationToken);
        var requiredScope = command.ScopeKind == AccessGrantScopeKind.DeploymentWide
            ? "DeploymentWide" : "ResourceScoped";
        if (!scopes.Any(permission => ProductPermissionCatalog.Definitions.Any(
            p => p.Key == permission && p.ScopeKind == requiredScope)))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_SCOPE_INVALID",
                FailureKind.Validation,
                "The selected role has no permissions for that scope.");
        }

        // Prevent uncontrolled duplicate grants; administrators should edit
        // the existing assignment rather than create indistinguishable rows.
        var existing = await dbContext.AccessGrants.AsNoTracking()
            .Where(item => item.SubjectKind == "User" && item.UserId == userId &&
                item.RoleId == command.RoleId &&
                item.ScopeKind == command.ScopeKind.ToString() && item.IsActive)
            .AnyAsync(cancellationToken);
        if (existing)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT",
                FailureKind.Conflict,
                "An active assignment for this role and scope already exists.");
        }

        var id = Guid.NewGuid();
        dbContext.AccessGrants.Add(new AccessGrantEntity
        {
            Id = id,
            SubjectKind = "User",
            UserId = userId,
            RoleId = role.Id,
            ScopeKind = command.ScopeKind.ToString(),
            IsActive = true,
            Revision = 1,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        });
        foreach (var assetId in scopeIds)
        {
            dbContext.AccessGrantAssets.Add(new AccessGrantAssetEntity
            {
                GrantId = id,
                AssetId = assetId
            });
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = nowUtc,
            ActorUserId = actor.UserId,
            ActorIdentity = actor.IdentityName,
            Action = "security.grant.created",
            TargetType = "security-access-grant",
            TargetId = id.ToString("D"),
            CorrelationId = actor.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                userId, roleId = role.Id,
                scopeKind = command.ScopeKind.ToString(),
                assetIds = scopeIds,
                liveAuthorizationUnchanged = true
            }, AuditOptions)
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState is PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "A concurrent security change occurred. Reload before retrying.",
                innerException: exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "An active matching grant already exists. Reload before retrying.",
                innerException: exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is PostgresException postgres &&
                postgres.SqlState is
                    PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Concurrent grant creation interrupted the transaction. Reload before retrying.",
                innerException: exception);
        }

        return new AccessGrant(id, AccessGrantSubjectKind.User,
            userId, null, role.Id, command.ScopeKind, scopeIds, true, 1);
    }

    public async Task<AccessGrant> DeactivateForUserAsync(
        Guid userId, Guid grantId, long expectedRevision,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        nowUtc = nowUtc.ToUniversalTime();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var authorized = actor.UserId is { } adminId && adminId != userId &&
            await dbContext.Users.AsNoTracking().AnyAsync(
                user => user.Id == adminId && user.IsActive, cancellationToken) &&
            await dbContext.UserRoles.AsNoTracking().AnyAsync(
                role => role.UserId == adminId && role.Role == "Admin",
                cancellationToken);
        if (!authorized)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active, distinct administrator is required.");
        }
        if (!await dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId && user.IsActive, cancellationToken))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE", FailureKind.NotFound,
                "The target user is not active.");
        }
        var current = await dbContext.AccessGrants.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == grantId &&
                item.SubjectKind == "User" && item.UserId == userId,
                cancellationToken);
        if (current is null)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_NOT_FOUND", FailureKind.NotFound,
                "Assignment not found for the specified user.");
        }
        if (!current.IsActive || current.Revision != expectedRevision)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Assignment revision changed. Reload before editing.");
        }

        try
        {
            // Atomic compare-and-set prevents stale writers and double audit.
            var changed = await dbContext.AccessGrants
                .Where(item => item.Id == grantId &&
                    item.SubjectKind == "User" && item.UserId == userId &&
                    item.IsActive && item.Revision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsActive, false)
                    .SetProperty(item => item.Revision, item => item.Revision + 1)
                    .SetProperty(item => item.UpdatedAtUtc, nowUtc),
                    cancellationToken);
            if (changed != 1)
            {
                throw new WellSiteAutoPilotException(
                    "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                    "Assignment revision changed. Reload before editing.");
            }

            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = nowUtc,
                ActorUserId = actor.UserId,
                ActorIdentity = actor.IdentityName,
                Action = "security.grant.deactivated",
                TargetType = "security-access-grant",
                TargetId = grantId.ToString("D"),
                CorrelationId = actor.CorrelationId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    userId, roleId = current.RoleId,
                    scopeKind = current.ScopeKind,
                    previousRevision = expectedRevision,
                    newRevision = expectedRevision + 1,
                    liveAuthorizationUnchanged = true
                }, AuditOptions)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState is PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Concurrent security change. Reload before retrying.",
                innerException: exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is PostgresException postgres &&
                postgres.SqlState is
                    PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_GRANT_CONFLICT", FailureKind.Conflict,
                "Concurrent security change. Reload before retrying.",
                innerException: exception);
        }

        var assets = await dbContext.AccessGrantAssets.AsNoTracking()
            .Where(item => item.GrantId == grantId)
            .OrderBy(item => item.AssetId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);
        return new AccessGrant(current.Id, AccessGrantSubjectKind.User,
            current.UserId, null, current.RoleId,
            Enum.Parse<AccessGrantScopeKind>(current.ScopeKind),
            assets, false, expectedRevision + 1);
    }

    private static AccessGrant ToDomain(AccessGrantEntity row,
        IEnumerable<Guid> assetIds)
    {
        if (!Enum.TryParse<AccessGrantSubjectKind>(row.SubjectKind, out var subject) ||
            !Enum.TryParse<AccessGrantScopeKind>(row.ScopeKind, out var scope))
        {
            throw new InvalidOperationException("Unsupported persisted grant type.");
        }

        return new AccessGrant(row.Id, subject, row.UserId, row.GroupSid,
            row.RoleId, scope, assetIds.ToArray(), row.IsActive, row.Revision);
    }
}
