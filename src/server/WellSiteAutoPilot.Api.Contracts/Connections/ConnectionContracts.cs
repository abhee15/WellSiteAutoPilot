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
    IReadOnlyList<string> Capabilities);

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
