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
    DateTimeOffset CreatedAtUtc,
    string? HiddenNote,
    DateTimeOffset? HiddenAtUtc,
    string? HiddenBy);

/// <summary>An optional note of up to 200 characters explaining why the type is hidden.</summary>
public sealed record HideAssetTypeRequest(string? Note);

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
    DateTimeOffset CreatedAtUtc,
    Guid? OwnerConnectionId = null);

/// <summary>Presentation hint only; POST /assets remains authoritative.</summary>
public sealed record AssetCreationCapabilitiesResponse(
    bool CanCreateRoot,
    bool CanCreateUnderAuthorizedParents);

/// <summary>One authorized asset in the hierarchy, with how many authorized assets sit directly below it.</summary>
public sealed record AssetTreeNodeResponse(
    Guid Id,
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    bool IsActive,
    int ChildCount);

public sealed record AssetPathSegmentResponse(Guid Id, string Name);

/// <summary>A search match with the authorized ancestors it sits under, nearest the top first.</summary>
public sealed record AssetSearchResultResponse(
    Guid Id,
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    bool IsActive,
    AssetPathSegmentResponse[] Path);
