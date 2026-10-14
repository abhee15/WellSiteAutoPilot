namespace WellSiteAutoPilot.Persistence.Security;

public sealed class RolePermissionEntity
{
    public Guid RoleId { get; init; }
    public string Permission { get; init; } = string.Empty;
}
