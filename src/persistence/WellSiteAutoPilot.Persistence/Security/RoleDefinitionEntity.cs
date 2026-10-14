namespace WellSiteAutoPilot.Persistence.Security;

public sealed class RoleDefinitionEntity
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; init; }
    public bool IsActive { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
