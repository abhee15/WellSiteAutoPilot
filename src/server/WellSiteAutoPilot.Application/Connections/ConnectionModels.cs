using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>The last time someone tried a connection. Only meaningful for the revision that was tried.</summary>
public sealed record ConnectionTestRecord(
    string Outcome, string Message, string? ActingAs, DateTimeOffset TestedAtUtc, int TestedRevision);

/// <summary>
/// A named, saved way to reach one outside system. The settings are opaque to the Server: the adapter declared
/// what they mean and the Gateway judged them.
/// </summary>
public sealed record ConnectionRecord(
    Guid Id,
    string Name,
    string AdapterKey,
    int SettingsVersion,
    IReadOnlyDictionary<string, string> Settings,
    bool IsEnabled,
    int Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ConnectionTestRecord? LastTest);

public sealed record CreateConnectionCommand(
    string Name, string AdapterKey, IReadOnlyDictionary<string, string?>? Settings);

public sealed record UpdateConnectionCommand(
    int ExpectedRevision, string Name, IReadOnlyDictionary<string, string?>? Settings);

public static class ConnectionFailureCodes
{
    public const string NameConflict = "CONNECTION_NAME_CONFLICT";
    public const string StaleRevision = "CONNECTION_REVISION_STALE";
    public const string NotFound = "CONNECTION_NOT_FOUND";
    public const string SettingsInvalid = "CONNECTION_SETTINGS_INVALID";
    public const string AdapterUnknown = "CONNECTION_ADAPTER_UNKNOWN";
    public const string Disabled = "CONNECTION_DISABLED";
    public const string DiscoveryUnsupported = "CONNECTION_DISCOVERY_UNSUPPORTED";
    public const string DiscoveryTooLarge = "CONNECTION_DISCOVERY_TOO_LARGE";
    public const string DiscoveryIncomplete = "CONNECTION_DISCOVERY_INCOMPLETE";
    public const string ImportLevelsInvalid = "CONNECTION_IMPORT_LEVELS_INVALID";
    public const string HasImports = "CONNECTION_HAS_IMPORTS";
    public const string ImportPlanChanged = "CONNECTION_IMPORT_PLAN_CHANGED";
    public const string ImportNotAllowed = "CONNECTION_IMPORT_NOT_ALLOWED";
    public const string ImportNeedsAllAssets = "CONNECTION_IMPORT_NEEDS_ALL_ASSETS";
}

/// <summary>What the Server asks of the Gateway about adapters. The Gateway is authoritative; the Server stays provider-neutral.</summary>
public interface IGatewayAdapters
{
    Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the Gateway does not know the adapter.</summary>
    Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default);

    /// <summary>Null when the Gateway does not know the adapter.</summary>
    Task<AdapterTestResult?> TestAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default);

    /// <summary>Null when the Gateway does not know the adapter or it cannot discover.</summary>
    Task<DiscoveryResult?> DiscoverAsync(
        string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default);
}
