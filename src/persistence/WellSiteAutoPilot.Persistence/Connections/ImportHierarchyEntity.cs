namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class ImportHierarchyEntity
{
    public Guid ConnectionId { get; init; }
    public int Revision { get; init; } = 1;
    public string LevelsJson { get; init; } = "[]";
    public string FieldsJson { get; init; } = "[]";
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class ImportedGroupEntity
{
    public Guid ConnectionId { get; init; }
    public string GroupKey { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
}
