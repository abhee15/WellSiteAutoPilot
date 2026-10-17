namespace WellSiteAutoPilot.Domain.Security;

/// <summary>Persisted identity metadata. Effective authority comes from paired grants.</summary>
public sealed record UserIdentity(Guid Id, string IdentityKey, string IdentityName, string? DisplayName,
    bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc);
