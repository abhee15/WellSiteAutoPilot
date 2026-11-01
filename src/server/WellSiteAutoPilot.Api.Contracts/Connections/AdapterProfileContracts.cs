namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterCapabilityResponse(string Kind, string Name, string ContractVersion);

/// <summary>
/// One quantity an adapter can read. <c>Unit</c> is the adapter's default; a connection's point mapping may change it.
/// <c>SchemaId</c> and <c>RequiredCapability</c> are set for structured values.
/// </summary>
public sealed record AdapterQuantityResponse(
    string Name, string Label, string ValueKind, string? Unit, string TimestampMeaning,
    string? SchemaId, string? RequiredCapability, bool NeedsMapping);

public sealed record AdapterLimitResponse(string Key, string Label, string Value);

public sealed record AdapterConnectionSummaryResponse(
    int Total, int Enabled, int Healthy, int NeedsAttention, int Unreachable, int NotChecked);

public sealed record AdapterExampleResponse(string StarterId, string Label, string Description, IReadOnlyList<string> Quantities);

/// <summary>
/// What one adapter exposes, for people connecting to it and authoring logic against it. Built from what the adapter
/// declares; reading it never calls the outside system. <c>MinimumCadenceSeconds</c> is null when the adapter has not
/// declared how often it may be read.
/// </summary>
public sealed record AdapterProfileResponse(
    string Key,
    string DisplayName,
    string Description,
    int SettingsVersion,
    IReadOnlyList<AdapterSettingFieldResponse> Settings,
    IReadOnlyList<AdapterCapabilityResponse> Capabilities,
    bool CanDiscover,
    IReadOnlyList<DiscoveryFieldResponse> GroupingFields,
    bool CanRead,
    bool CanWrite,
    string? ResourceKind,
    IReadOnlyList<AdapterQuantityResponse> Quantities,
    int? MinimumCadenceSeconds,
    IReadOnlyList<AdapterLimitResponse> Limits,
    AdapterConnectionSummaryResponse Connections,
    IReadOnlyList<AdapterExampleResponse> Examples);
