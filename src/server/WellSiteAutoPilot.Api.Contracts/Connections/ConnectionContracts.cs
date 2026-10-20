namespace WellSiteAutoPilot.Api.Contracts.Connections;

public sealed record AdapterSettingFieldResponse(
    string Key, string Label, string HelpText, string Kind, bool Required, int MaxLength);

/// <summary>One kind of outside system this installation can connect to, as the adapter itself describes it.</summary>
public sealed record AdapterResponse(
    string Key,
    string DisplayName,
    string Description,
    int SettingsVersion,
    IReadOnlyList<AdapterSettingFieldResponse> Fields,
    IReadOnlyList<string> Capabilities,
    bool CanDiscover,
    IReadOnlyList<DiscoveryFieldResponse> GroupingFields);

public sealed record DiscoveryFieldResponse(string Key, string Label);

/// <summary>One object found in an outside system. Metadata holds the facts the adapter read, by name.</summary>
public sealed record DiscoveredObjectResponse(
    string ExternalId, string DisplayName, string Kind, IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// What a connection holds right now. When <c>Complete</c> is false the list is partial or empty and must not be
/// read as "nothing there". <c>ActingAs</c> is the account the outside system saw.
/// </summary>
public sealed record DiscoveryResponse(
    bool Complete, IReadOnlyList<DiscoveredObjectResponse> Items, IReadOnlyList<string> Warnings, string? ActingAs);

public sealed record ConnectionTestResponse(
    string Outcome, string Message, string? ActingAs, DateTimeOffset TestedAtUtc, int TestedRevision);

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
public sealed record ImportPreviewRequest(List<string>? Levels);

public sealed record ImportGroupResponse(
    string Name, bool IsFallback, int ObjectCount, IReadOnlyList<string> SampleNames, IReadOnlyList<ImportGroupResponse> Children);

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
    int AlreadyImported);

/// <summary>Applies the plan the person reviewed: the same levels and the fingerprint the preview returned.</summary>
public sealed record ImportConnectionRequest(List<string>? Levels, string? ExpectedFingerprint);

public sealed record ImportResultResponse(
    int WellsCreated, int GroupsCreated, int GroupsReused, int AlreadyImported, IReadOnlyList<string> TypesCreated);
