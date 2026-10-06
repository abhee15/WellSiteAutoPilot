namespace WellSiteAutoPilot.Persistence.Assets;

public sealed class AssetTypeEntity
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string AttributeSchemaJson { get; set; } = "{}";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
