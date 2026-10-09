using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Persistence.Audit;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class UserAccessRepository(
    WellSiteAutoPilotDbContext dbContext) : IUserAccessRepository
{
    private static readonly TimeSpan LastSeenWriteInterval =
        TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);
    public async Task<UserAccessProfile?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        return user is null
            ? null
            : await LoadProfileAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserAccessProfile>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(item => item.IdentityName)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        if (users.Length == 0)
        {
            return [];
        }

        var userIds = users.Select(item => item.Id).ToArray();

        var roles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(item => userIds.Contains(item.UserId))
            .OrderBy(item => item.Role)
            .ToArrayAsync(cancellationToken);

        var scopes = await dbContext.UserAssetScopes
            .AsNoTracking()
            .Where(item => userIds.Contains(item.UserId))
            .OrderBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return users
            .Select(user => ToDomain(
                user,
                roles.Where(item => item.UserId == user.Id),
                scopes.Where(item => item.UserId == user.Id)))
            .ToArray();
    }

    public async Task<UserAccessProfile> UpsertAuthenticatedUserAsync(
        string identityKey,
        string identityName,
        string normalizedIdentityName,
        string? displayName,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIdentityName);

        seenAtUtc = seenAtUtc.ToUniversalTime();

        var user = await dbContext.Users
            .SingleOrDefaultAsync(
                item => item.IdentityKey == identityKey,
                cancellationToken);

        if (user is null)
        {
            var candidate = new UserEntity
            {
                Id = Guid.NewGuid(),
                IdentityKey = identityKey,
                IdentityName = identityName,
                NormalizedIdentityName = normalizedIdentityName,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = seenAtUtc,
                LastSeenAtUtc = seenAtUtc
            };

            dbContext.Users.Add(candidate);

            var provisioningAudit = CreateAuditEvent(
                candidate.Id,
                identityName,
                "security.user.provisioned",
                "security-user",
                candidate.Id.ToString(),
                null,
                seenAtUtc,
                null,
                new
                {
                    candidate.IdentityKey,
                    candidate.NormalizedIdentityName
                });
            dbContext.AuditEvents.Add(provisioningAudit);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                user = candidate;
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(candidate).State = EntityState.Detached;
                dbContext.Entry(provisioningAudit).State = EntityState.Detached;

                user = await dbContext.Users
                    .SingleOrDefaultAsync(
                        item =>
                            item.IdentityKey ==
                            identityKey,
                        cancellationToken);

                if (user is null)
                {
                    throw;
                }
            }
        }

        var changed = false;

        if (!string.Equals(
                user.IdentityName,
                identityName,
                StringComparison.Ordinal))
        {
            user.IdentityName = identityName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(displayName) &&
            !string.Equals(
                user.DisplayName,
                displayName,
                StringComparison.Ordinal))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (seenAtUtc - user.LastSeenAtUtc >= LastSeenWriteInterval)
        {
            user.LastSeenAtUtc = seenAtUtc;
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await LoadProfileAsync(user, cancellationToken);
    }

    public async Task ReplaceAccessAsync(
        Guid userId,
        IReadOnlyCollection<ApplicationRole> roles,
        IReadOnlyCollection<Guid> assetScopeIds,
        bool preserveLastAdministrator,
        SecurityActorContext actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(assetScopeIds);
        ArgumentNullException.ThrowIfNull(actor);

        const int maximumAttempts = 3;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                await ReplaceAccessAttemptAsync(
                    userId,
                    roles,
                    assetScopeIds,
                    preserveLastAdministrator,
                    actor,
                    occurredAtUtc,
                    cancellationToken);
                return;
            }
            catch (PostgresException exception)
                when (exception.SqlState == PostgresErrorCodes.SerializationFailure &&
                      attempt < maximumAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException(
            "Security access update exhausted serialization retry attempts.");
    }

    private async Task ReplaceAccessAttemptAsync(
        Guid userId,
        IReadOnlyCollection<ApplicationRole> roles,
        IReadOnlyCollection<Guid> assetScopeIds,
        bool preserveLastAdministrator,
        SecurityActorContext actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var exists = await dbContext.Users.AnyAsync(
            item => item.Id == userId && item.IsActive,
            cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException(
                $"User {userId} does not exist or is inactive.");
        }

        var previousRoles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Role)
            .Select(item => item.Role)
            .ToArrayAsync(cancellationToken);

        var previousAssetScopeIds = await dbContext.UserAssetScopes
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.AssetId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        if (preserveLastAdministrator)
        {
            var adminRole = ApplicationRole.Admin.ToString();
            var activeAdministratorCount = await dbContext.UserRoles
                .Where(item => item.Role == adminRole)
                .Join(
                    dbContext.Users.Where(item => item.IsActive),
                    roleAssignment => roleAssignment.UserId,
                    user => user.Id,
                    (_, _) => 1)
                .CountAsync(cancellationToken);

            if (activeAdministratorCount <= 1)
            {
                throw new LastAdministratorRequiredException();
            }
        }

        var scopeIds = assetScopeIds.Distinct().ToArray();
        if (scopeIds.Length > 0)
        {
            var existingAssetCount = await dbContext.Assets.CountAsync(
                item => scopeIds.Contains(item.Id) && item.IsActive,
                cancellationToken);

            if (existingAssetCount != scopeIds.Length)
            {
                throw new InvalidOperationException(
                    "One or more assigned Asset scopes do not reference active Assets.");
            }
        }

        await dbContext.UserRoles
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserAssetScopes
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var role in roles.Distinct())
        {
            dbContext.UserRoles.Add(new UserRoleEntity
            {
                UserId = userId,
                Role = role.ToString()
            });
        }

        foreach (var assetId in scopeIds)
        {
            dbContext.UserAssetScopes.Add(new UserAssetScopeEntity
            {
                UserId = userId,
                AssetId = assetId
            });
        }

        dbContext.AuditEvents.Add(
            CreateAuditEvent(
                actor.UserId,
                actor.IdentityName,
                "security.user.access-replaced",
                "security-user",
                userId.ToString(),
                null,
                occurredAtUtc,
                actor.CorrelationId,
                new
                {
                    previousRoles,
                    roles = roles
                        .Distinct()
                        .Select(item => item.ToString())
                        .OrderBy(item => item)
                        .ToArray(),
                    previousAssetScopeIds,
                    assetScopeIds = scopeIds.OrderBy(item => item).ToArray()
                }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> HasActiveUserInRoleAsync(
        ApplicationRole role,
        CancellationToken cancellationToken = default)
    {
        var roleName = role.ToString();

        return dbContext.UserRoles
            .Where(item => item.Role == roleName)
            .Join(
                dbContext.Users.Where(item => item.IsActive),
                roleAssignment => roleAssignment.UserId,
                user => user.Id,
                (_, _) => 1)
            .AnyAsync(cancellationToken);
    }

    public async Task EnsureRoleAsync(
        Guid userId,
        ApplicationRole role,
        SecurityActorContext actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var roleName = role.ToString();

        if (await dbContext.UserRoles.AnyAsync(
                item => item.UserId == userId && item.Role == roleName,
                cancellationToken))
        {
            return;
        }

        var assignment = new UserRoleEntity
        {
            UserId = userId,
            Role = roleName
        };
        dbContext.UserRoles.Add(assignment);

        var roleAudit = CreateAuditEvent(
            actor.UserId,
            actor.IdentityName,
            actor.IdentityName == "system:bootstrap" &&
            role == ApplicationRole.Admin
                ? "security.bootstrap-admin.assigned"
                : "security.role.assigned",
            "security-user",
            userId.ToString(),
            null,
            occurredAtUtc,
            actor.CorrelationId,
            new
            {
                Role = roleName
            });
        dbContext.AuditEvents.Add(roleAudit);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(assignment).State = EntityState.Detached;
            dbContext.Entry(roleAudit).State = EntityState.Detached;

            if (!await dbContext.UserRoles.AnyAsync(
                    item =>
                        item.UserId == userId &&
                        item.Role == roleName,
                    cancellationToken))
            {
                throw;
            }
        }
    }

    private static AuditEventEntity CreateAuditEvent(
        Guid? actorUserId,
        string actorIdentity,
        string action,
        string targetType,
        string? targetId,
        Guid? assetId,
        DateTimeOffset occurredAtUtc,
        string? correlationId,
        object details) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
            ActorUserId = actorUserId,
            ActorIdentity = actorIdentity,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            AssetId = assetId,
            CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(
                details,
                AuditJsonOptions)
        };

    private async Task<UserAccessProfile> LoadProfileAsync(
        UserEntity user,
        CancellationToken cancellationToken)
    {
        var roles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.Role)
            .Select(item => item.Role)
            .ToArrayAsync(cancellationToken);

        var scopes = await dbContext.UserAssetScopes
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.AssetId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return ToDomain(
            user,
            roles.Select(role => new UserRoleEntity
            {
                UserId = user.Id,
                Role = role
            }),
            scopes.Select(assetId => new UserAssetScopeEntity
            {
                UserId = user.Id,
                AssetId = assetId
            }));
    }

    private static UserAccessProfile ToDomain(
        UserEntity user,
        IEnumerable<UserRoleEntity> roles,
        IEnumerable<UserAssetScopeEntity> scopes) =>
        new(
            user.Id,
            user.IdentityKey,
            user.IdentityName,
            user.NormalizedIdentityName,
            user.DisplayName,
            user.IsActive,
            user.CreatedAtUtc,
            user.LastSeenAtUtc,
            roles
                .Select(item => Enum.Parse<ApplicationRole>(item.Role))
                .Distinct()
                .OrderBy(item => item)
                .ToArray(),
            scopes
                .Select(item => item.AssetId)
                .Distinct()
                .OrderBy(item => item)
                .ToArray());
}
