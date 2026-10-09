namespace WellSiteAutoPilot.Persistence.Security;

public sealed class UserEntity
{
    public Guid Id { get; init; }
    public string IdentityName { get; set; } = string.Empty;
    public string NormalizedIdentityName { get; init; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
