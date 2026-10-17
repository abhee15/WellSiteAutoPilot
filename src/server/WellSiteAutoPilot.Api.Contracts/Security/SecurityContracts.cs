namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record CurrentUserResponse(Guid UserId, string IdentityName, string? DisplayName,
    bool IsActive, IReadOnlyCollection<string> Permissions);

/// <param name="ActiveDirectGrantCount">
/// Active role grants assigned to this user directly. Access that comes through a Windows
/// group is proven at sign-in and is not counted here.
/// </param>
public sealed record SecurityUserResponse(Guid UserId, string IdentityName, string? DisplayName,
    bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc, int ActiveDirectGrantCount);
