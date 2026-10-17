using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public interface IUserIdentityRepository
{
    Task<UserIdentity?> GetIdentityAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UserIdentity>> ListIdentitiesAsync(int limit, CancellationToken cancellationToken = default);
    Task<UserIdentity> UpsertIdentityAsync(string identityKey, string identityName, string normalizedIdentityName,
        string? displayName, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default);
}
