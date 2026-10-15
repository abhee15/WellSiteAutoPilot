namespace WellSiteAutoPilot.Api.Contracts.Assets;

public sealed record CreateAssetTypeRequest(
    string Key,
    string DisplayName,
    string AttributeSchemaJson);

public sealed record AssetTypeResponse(
    Guid Id,
    string Key,
    string DisplayName,
    int SchemaVersion,
    string AttributeSchemaJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateAssetRequest(
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    string AttributeValuesJson);

public sealed record AssetResponse(
    Guid Id,
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    string AttributeValuesJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>Presentation hint only; POST /assets remains authoritative.</summary>
public sealed record AssetCreationCapabilitiesResponse(
    bool CanCreateRoot,
    bool CanCreateUnderAuthorizedParents);
