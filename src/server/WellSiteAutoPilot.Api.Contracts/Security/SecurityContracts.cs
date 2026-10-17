namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record CurrentUserResponse(Guid UserId, string IdentityName, string? DisplayName,
    bool IsActive, IReadOnlyCollection<string> Permissions);

public sealed record SecurityUserResponse(Guid UserId, string IdentityName, string? DisplayName,
    bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc);
