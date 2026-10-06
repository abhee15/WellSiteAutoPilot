namespace WellSiteAutoPilot.Domain.Assets;

public sealed record AssetTypeDefinition(
    Guid Id,
    string Key,
    string DisplayName,
    int SchemaVersion,
    string AttributeSchemaJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
