namespace WellSiteAutoPilot.Persistence.Security;

public sealed class AccessGrantEntity
{
    public Guid Id { get; init; }
    public string SubjectKind { get; init; } = "User";
    public Guid? UserId { get; init; }
    public string? GroupSid { get; init; }
    public Guid RoleId { get; init; }
    public string ScopeKind { get; init; } = string.Empty;
    public bool IsActive { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AccessGrantAssetEntity
{
    public Guid GrantId { get; init; }
    public Guid AssetId { get; init; }
}
