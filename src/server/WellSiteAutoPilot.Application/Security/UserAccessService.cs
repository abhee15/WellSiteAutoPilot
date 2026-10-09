using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed class UserAccessService(
    IUserAccessRepository repository,
    TimeProvider timeProvider)
{
    public async Task<UserAccessProfile> ResolveAuthenticatedAsync(
        string identityName,
        string? displayName,
        IReadOnlySet<string> bootstrapAdministratorIdentities,
        CancellationToken cancellationToken = default)
    {
        var normalizedIdentity = NormalizeIdentity(identityName);
        var nowUtc = timeProvider.GetUtcNow();

        var profile = await repository.UpsertAuthenticatedUserAsync(
            identityName.Trim(),
            normalizedIdentity,
            string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            nowUtc,
            cancellationToken);

        if (bootstrapAdministratorIdentities.Contains(normalizedIdentity) &&
            !profile.Roles.Contains(ApplicationRole.Admin))
        {
            await repository.EnsureRoleAsync(
                profile.Id,
                ApplicationRole.Admin,
                cancellationToken);

            profile = await repository.GetAsync(profile.Id, cancellationToken) ??
                      throw new InvalidOperationException(
                          "Provisioned user could not be reloaded.");
        }

        return profile;
    }

    public async Task<UserAccessProfile> GetRequiredAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAsync(userId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "SECURITY_USER_NOT_FOUND",
            FailureKind.NotFound,
            "The requested user was not found.");

    public Task<IReadOnlyCollection<UserAccessProfile>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Security user list limit must be between 1 and 500.");
        }

        return repository.ListAsync(limit, cancellationToken);
    }

    public async Task<UserAccessProfile> ReplaceAccessAsync(
        Guid userId,
        IReadOnlyCollection<ApplicationRole> roles,
        IReadOnlyCollection<Guid> assetScopeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(assetScopeIds);

        var existing = await GetRequiredAsync(userId, cancellationToken);
        var normalizedRoles = roles.Distinct().OrderBy(item => item).ToArray();
        var normalizedAssets = assetScopeIds
            .Where(item => item != Guid.Empty)
            .Distinct()
            .OrderBy(item => item)
            .ToArray();

        if (existing.Roles.Contains(ApplicationRole.Admin) &&
            !normalizedRoles.Contains(ApplicationRole.Admin) &&
            await repository.CountActiveUsersInRoleAsync(
                ApplicationRole.Admin,
                cancellationToken) <= 1)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_LAST_ADMIN_REQUIRED",
                FailureKind.Conflict,
                "The last active administrator cannot lose the Admin role.");
        }

        await repository.ReplaceAccessAsync(
            userId,
            normalizedRoles,
            normalizedAssets,
            cancellationToken);

        return await GetRequiredAsync(userId, cancellationToken);
    }

    public static string NormalizeIdentity(string identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_IDENTITY_REQUIRED",
                FailureKind.Authorization,
                "An authenticated Windows identity is required.");
        }

        return identityName.Trim().ToUpperInvariant();
    }
}
