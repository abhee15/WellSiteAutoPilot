namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record CurrentUserResponse(
    Guid UserId,
    string IdentityName,
    string? DisplayName,
    bool IsActive,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssetScopeIds);

public sealed record SecurityUserResponse(
    Guid UserId,
    string IdentityName,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssetScopeIds);

public sealed record ReplaceUserAccessRequest(
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssetScopeIds);
