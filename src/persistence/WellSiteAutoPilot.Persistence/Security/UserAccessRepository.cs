using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Persistence.Security;

public sealed class UserAccessRepository(
    WellSiteAutoPilotDbContext dbContext) : IUserAccessRepository
{
    private static readonly TimeSpan LastSeenWriteInterval =
        TimeSpan.FromMinutes(5);
    public async Task<UserAccessProfile?> GetByIdentityAsync(
        string normalizedIdentityName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIdentityName);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.NormalizedIdentityName == normalizedIdentityName,
                cancellationToken);

        return user is null
            ? null
            : await LoadProfileAsync(user, cancellationToken);
    }

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
        string identityName,
        string normalizedIdentityName,
        string? displayName,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIdentityName);

        seenAtUtc = seenAtUtc.ToUniversalTime();

        var user = await dbContext.Users
            .SingleOrDefaultAsync(
                item => item.NormalizedIdentityName == normalizedIdentityName,
                cancellationToken);

        if (user is null)
        {
            var candidate = new UserEntity
            {
                Id = Guid.NewGuid(),
                IdentityName = identityName,
                NormalizedIdentityName = normalizedIdentityName,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = seenAtUtc,
                LastSeenAtUtc = seenAtUtc
            };

            dbContext.Users.Add(candidate);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                user = candidate;
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(candidate).State = EntityState.Detached;

                user = await dbContext.Users
                    .SingleOrDefaultAsync(
                        item =>
                            item.NormalizedIdentityName ==
                            normalizedIdentityName,
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(assetScopeIds);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var exists = await dbContext.Users.AnyAsync(
            item => item.Id == userId && item.IsActive,
            cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException($"User {userId} does not exist or is inactive.");
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

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EnsureRoleAsync(
        Guid userId,
        ApplicationRole role,
        CancellationToken cancellationToken = default)
    {
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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(assignment).State = EntityState.Detached;

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

    public Task<int> CountActiveUsersInRoleAsync(
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
            .CountAsync(cancellationToken);
    }

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
