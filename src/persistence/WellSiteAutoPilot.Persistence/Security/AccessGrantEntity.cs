namespace WellSiteAutoPilot.Persistence.Security;

public sealed class AccessGrantEntity
{
    public Guid Id { get; init; }
    public string SubjectKind { get; init; } = "User";
    public Guid? UserId { get; init; }
    public string? GroupDisplayName { get; init; }
    public string? GroupDomain { get; init; }
    private string? directoryId;
    public string? DirectoryId
    {
        get => directoryId ?? (SubjectKind == "Group" ? "windows-ad" : null);
        init => directoryId = value;
    }
    public string? GroupId { get; init; }
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
