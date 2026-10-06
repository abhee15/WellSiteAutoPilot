namespace WellSiteAutoPilot.Application.Assets;

public sealed record CreateAssetTypeCommand(
    string Key,
    string DisplayName,
    string AttributeSchemaJson);

public sealed record CreateAssetCommand(
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    string AttributeValuesJson);
