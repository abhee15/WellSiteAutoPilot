namespace WellSiteAutoPilot.Domain.Assets;

public sealed record Asset(
    Guid Id,
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    string AttributeValuesJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
