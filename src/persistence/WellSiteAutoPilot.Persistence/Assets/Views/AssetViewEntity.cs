namespace WellSiteAutoPilot.Persistence.Assets.Views;

public sealed class AssetViewEntity
{
    public Guid Id { get; init; }
    public Guid OwnerUserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "{}";
    public int Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
