namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterSettingFieldResponse(
    string Key, string Label, string HelpText, string Kind, bool Required, int MaxLength);

/// <summary>
/// One command an adapter can carry out: its shape, never a limit (limits are set per connection by an engineer).
/// <c>ValueKind</c> is Number, WholeNumber or OnOff; <c>Verification</c> is ReadBack or AcknowledgementOnly.
/// </summary>
public sealed record AdapterCommandResponse(
    string Name,
    string DisplayName,
    string Description,
    string Quantity,
    string? Unit,
    string ValueKind,
    IReadOnlyList<AdapterSettingFieldResponse> TargetFields,
    string Verification,
    string? ReadbackQuantity,
    int DefaultSettleSeconds,
    int MaximumSettleSeconds,
    int DefaultDeadlineSeconds,
    int MaximumDeadlineSeconds);

/// <summary>One kind of outside system this installation can connect to, as the adapter itself describes it.</summary>
public sealed record AdapterResponse(
    string Key,
    string DisplayName,
    string Description,
    int SettingsVersion,
    IReadOnlyList<AdapterSettingFieldResponse> Fields,
    IReadOnlyList<string> Capabilities,
    bool CanDiscover,
    IReadOnlyList<DiscoveryFieldResponse> GroupingFields,
    IReadOnlyList<AdapterCommandResponse> Commands);

public sealed record DiscoveryFieldResponse(string Key, string Label);

/// <summary>One object found in an outside system. Metadata holds the facts the adapter read, by name.</summary>
public sealed record DiscoveredObjectResponse(
    string ExternalId, string DisplayName, string Kind, IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// What a connection holds right now. When <c>Complete</c> is false the list is partial or empty and must not be
/// read as "nothing there". <c>ActingAs</c> is the account the outside system saw.
/// </summary>
public sealed record DiscoveryResponse(
    bool Complete, IReadOnlyList<DiscoveredObjectResponse> Items, IReadOnlyList<string> Warnings, string? ActingAs,
    int TotalCount, Guid SnapshotId, IReadOnlyList<DiscoveryFacetResponse> Facets, DateTimeOffset ExpiresAtUtc,
    int? SourceAssetCount = null, ImportHierarchyResponse? Hierarchy = null);

public sealed record ImportHierarchyResponse(string Status, IReadOnlyList<string>? Levels);

public sealed record DiscoveryFacetValueResponse(string Value, int Count);

/// <summary>The values one grouping fact takes across everything found. <c>HasMore</c> is true when the list was cut short.</summary>
public sealed record DiscoveryFacetResponse(
    string Key, string Label, IReadOnlyList<DiscoveryFacetValueResponse> Values, bool HasMore, int? KnownCount = null);

/// <summary>Looks within what was found earlier. Words must all match; within one fact any chosen value matches, across facts all must.</summary>
public sealed record DiscoverySearchRequest(Guid SnapshotId, string? Query, Dictionary<string, List<string>>? Facets,
    int Offset = 0, int Limit = 1000);

public sealed record DiscoveryObjectKeyRequest(string Kind, string ExternalId);

/// <summary><c>Items</c> is one stable page of up to 1,000 matches; <c>MatchCount</c> counts all matches.</summary>
public sealed record DiscoverySearchResponse(int MatchCount, IReadOnlyList<DiscoveredObjectResponse> Items);

public sealed record ConnectionTestResponse(
    string Outcome, string Message, string? ActingAs, DateTimeOffset TestedAtUtc, int TestedRevision, string Source);

public sealed record ConnectionResponse(
    Guid Id,
    string Name,
    string AdapterKey,
    int SettingsVersion,
    IReadOnlyDictionary<string, string> Settings,
    bool IsEnabled,
    int Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ConnectionTestResponse? LastTest,
    /// <summary>False when the settings changed after the last test, so that result no longer describes them.</summary>
    bool LastTestIsCurrent);

public sealed record CreateConnectionRequest(string Name, string AdapterKey, Dictionary<string, string?>? Settings);

/// <summary>The adapter cannot change after creation. <c>ExpectedRevision</c> refuses an edit made from a stale copy.</summary>
public sealed record UpdateConnectionRequest(int ExpectedRevision, string Name, Dictionary<string, string?>? Settings);

public sealed record SetConnectionEnabledRequest(int ExpectedRevision, bool Enabled);

/// <summary>The facts to group by, outermost first. Empty means no groups, only the objects themselves.</summary>
public sealed record ImportPreviewRequest(
    List<string>? Levels, Guid? SnapshotId = null, string? Query = null, Dictionary<string, List<string>>? Facets = null,
    List<DiscoveryObjectKeyRequest>? Objects = null, bool AdoptExistingHierarchy = false);

public sealed record ImportGroupResponse(
    string Name, bool IsFallback, int ObjectCount, IReadOnlyList<string> SampleNames, IReadOnlyList<ImportGroupResponse> Children, string? Key = null, string? SourceIdentity = null);

/// <summary>
/// What an import would create, for review; nothing has been saved. <c>Fingerprint</c> identifies this exact plan so
/// applying it can refuse if the source changed since. <c>CanImport</c> is false while there are conflicts or a
/// missing permission, and <c>Warnings</c> and <c>Conflicts</c> say why in plain sentences.
/// </summary>
public sealed record ImportPreviewResponse(
    string Fingerprint,
    IReadOnlyList<string> Levels,
    int ObjectCount,
    int GroupCount,
    int FallbackObjectCount,
    int RootObjectCount,
    IReadOnlyList<ImportGroupResponse> Groups,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> TypesToCreate,
    bool CanImport,
    string? ActingAs,
    int AlreadyImported, bool AdoptingHierarchy = false, int ExistingWells = 0, int GroupsToAdopt = 0);

/// <summary>Applies the plan the person reviewed: the same levels and the fingerprint the preview returned.</summary>
public sealed record ImportConnectionRequest(
    List<string>? Levels, string? ExpectedFingerprint, Guid? SnapshotId = null, string? Query = null,
    Dictionary<string, List<string>>? Facets = null, List<DiscoveryObjectKeyRequest>? Objects = null, bool AdoptExistingHierarchy = false);

public sealed record ImportResultResponse(
    int WellsCreated, int GroupsCreated, int GroupsReused, int AlreadyImported, IReadOnlyList<string> TypesCreated, bool HierarchyAdopted = false);
