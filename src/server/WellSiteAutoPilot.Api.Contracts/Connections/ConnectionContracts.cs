namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterResponse(
    string Key, string DisplayName, string DefaultEndpoint, bool SupportsDiscovery, string Description);

public sealed record ConnectionRequest(string Name, string AdapterKey, string Endpoint, bool Enabled, int? Revision);

public sealed record ConnectionTestResponse(
    int TestedRevision, DateTimeOffset TestedAtUtc, string Health, string? HealthReason,
    string Compatibility, string? CompatibilityReason, bool SupportsDiscovery);

public sealed record ConnectionResponse(
    Guid Id, string Name, string AdapterKey, string Endpoint, bool Enabled, int Revision,
    DateTimeOffset UpdatedAtUtc, ConnectionTestResponse? LastTest);

public sealed record DiscoveredAssetResponse(
    string ExternalId, string DisplayName, string ProviderType, string? ParentExternalId,
    Dictionary<string, string> Metadata, bool AlreadyImported);

public sealed record DiscoveryResponse(
    Guid ConnectionId, DateTimeOffset DiscoveredAtUtc, DiscoveredAssetResponse[] Assets);

public sealed record HierarchyTemplateResponse(string Key, string Name, string[] Levels);

public sealed record ApplyDiscoveryRequest(string? TemplateKey, string[]? Levels);

public sealed record ApplyDiscoveryResponse(int Created, int AlreadyPresent, Guid AssetTypeId);
