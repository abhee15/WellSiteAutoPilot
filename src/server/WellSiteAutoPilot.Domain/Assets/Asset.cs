namespace WellSiteAutoPilot.Domain.Assets;

/// <param name="OwnerConnectionId">
/// The one Connection this Asset (or Group) belongs to, or null for a standalone Asset. Fixed when the Asset is
/// created; Assets of different Connections are always different Assets, whatever their names.
/// </param>
public sealed record Asset(
    Guid Id,
    Guid AssetTypeId,
    string Name,
    Guid? ParentAssetId,
    string AttributeValuesJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    Guid? OwnerConnectionId = null);
