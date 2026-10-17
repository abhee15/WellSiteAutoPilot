using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed class UserIdentityService(IUserIdentityRepository repository, TimeProvider clock)
{
    public Task<UserIdentity> ResolveAuthenticatedAsync(string identityKey, string identityName, string? displayName,
        CancellationToken cancellationToken = default)
    {
        var key = UserAccessService.NormalizeIdentityKey(identityKey);
        var name = UserAccessService.NormalizeIdentity(identityName);
        return repository.UpsertIdentityAsync(key, identityName.Trim(), name,
            string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(), clock.GetUtcNow(), cancellationToken);
    }

    public async Task<UserIdentity> GetRequiredAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await repository.GetIdentityAsync(userId, cancellationToken) ??
            throw new WellSiteAutoPilotException("SECURITY_USER_NOT_FOUND", FailureKind.NotFound, "The requested user was not found.");

    public Task<IReadOnlyCollection<UserIdentity>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Security user list limit must be between 1 and 500.");
        return repository.ListIdentitiesAsync(limit, cancellationToken);
    }
}
