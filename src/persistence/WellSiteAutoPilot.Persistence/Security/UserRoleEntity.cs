namespace WellSiteAutoPilot.Persistence.Security;

public sealed class UserRoleEntity
{
    public Guid UserId { get; init; }
    public string Role { get; init; } = string.Empty;
}
