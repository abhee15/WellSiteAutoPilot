namespace WellSiteAutoPilot.Persistence.Assets;

public sealed class AssetEntity
{
    public Guid Id { get; init; }
    public Guid AssetTypeId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentAssetId { get; set; }
    public string AttributeValuesJson { get; set; } = "{}";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
