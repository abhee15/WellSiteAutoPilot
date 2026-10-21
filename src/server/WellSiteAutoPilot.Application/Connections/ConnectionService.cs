using WellSiteAutoPilot.Application.Connections.Discovery;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

public sealed class ConnectionService(
    IConnectionRepository repository,
    IGatewayAdapters gateway,
    DiscoverySnapshotStore snapshots,
    TimeProvider timeProvider)
{
    public const int MaximumNameLength = 100;

    public Task<IReadOnlyList<AdapterDescriptor>> ListAdaptersAsync(CancellationToken cancellationToken = default) =>
        gateway.ListAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectionRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task<ConnectionRecord> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await repository.FindAsync(id, cancellationToken) ?? throw NotFound();

    public async Task<ConnectionRecord> CreateAsync(CreateConnectionCommand command, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = CleanName(command.Name);
        var adapterKey = (command.AdapterKey ?? string.Empty).Trim();
        var (settings, version) = await ValidateSettingsAsync(adapterKey, command.Settings, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var connection = new ConnectionRecord(Guid.NewGuid(), name, adapterKey, version, settings, true, 1, now, now, null);
        await repository.CreateAuthorizedAsync(connection, subject, actor, cancellationToken);
        return connection;
    }

    public async Task<ConnectionRecord> UpdateAsync(Guid id, UpdateConnectionCommand command, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var existing = await GetAsync(id, cancellationToken);
        var name = CleanName(command.Name);
        // The adapter is fixed when a connection is created; switching adapters means a new connection.
        var (settings, version) = await ValidateSettingsAsync(existing.AdapterKey, command.Settings, cancellationToken);
        return await repository.UpdateAuthorizedAsync(id, command.ExpectedRevision, name, version, settings, subject, actor,
            cancellationToken);
    }

    public Task<ConnectionRecord> SetEnabledAsync(Guid id, int expectedRevision, bool enabled, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        repository.SetEnabledAuthorizedAsync(id, expectedRevision, enabled, subject, actor, cancellationToken);

    public async Task<ConnectionRecord> TestAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetAsync(id, cancellationToken);
        if (!connection.IsEnabled)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.Disabled, FailureKind.Conflict,
                "Enable the connection before testing it.");
        var settings = connection.Settings.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.Ordinal);
        var result = await gateway.TestAsync(connection.AdapterKey, settings, cancellationToken)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.AdapterUnknown, FailureKind.Conflict,
                "This build no longer includes the adapter this connection uses.");
        if (!AdapterTestOutcomes.IsKnown(result.Outcome))
            throw new WellSiteAutoPilotException(FailureCodes.DependencyUnavailable, FailureKind.DependencyUnavailable,
                "The integration gateway returned an answer the server does not understand.");
        var record = new ConnectionTestRecord(result.Outcome, Truncate(result.Message, 500), Truncate(result.ActingAs, 256),
            timeProvider.GetUtcNow(), connection.Revision);
        return await repository.RecordTestAuthorizedAsync(id, record, subject, actor, cancellationToken);
    }

    /// <summary>
    /// Hard ceiling on one discovery. The cost is in transferring and holding the list, so this is generous; what one
    /// import may take is limited separately (<see cref="MaximumImportedObjects"/>).
    /// </summary>
    public const int MaximumDiscoveredObjects = 50000;

    /// <summary>One import writes everything in a single transaction, so it takes at most this many objects.</summary>
    public const int MaximumImportedObjects = 5000;

    /// <summary>How many found objects a person is shown at once; the rest is reached by searching and filtering.</summary>
    public const int SampleSize = 1000;

    public async Task<DiscoveryResult> DiscoverAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetAsync(id, cancellationToken);
        if (!connection.IsEnabled)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.Disabled, FailureKind.Conflict,
                "Enable the connection before finding what it holds.");
        var settings = connection.Settings.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.Ordinal);
        var result = await gateway.DiscoverAsync(connection.AdapterKey, settings, cancellationToken)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryUnsupported, FailureKind.Conflict,
                "This connection's adapter cannot list what it holds.");
        if (result.Items.Count > MaximumDiscoveredObjects)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.DiscoveryTooLarge, FailureKind.Validation,
                $"That system holds {result.Items.Count} objects, more than the {MaximumDiscoveredObjects} this can list at once. " +
                "Ask for a smaller set to be exposed to the connection's account, or tell the people who maintain this system.");
        await repository.RecordDiscoveryAuthorizedAsync(id, result.Items.Count, result.Warnings.Count, result.Complete,
            subject, actor, cancellationToken);
        return result;
    }

    /// <summary>
    /// Finds what the connection holds and keeps the answer for a while, so it can be searched, reviewed and imported
    /// without asking the outside system again.
    /// </summary>
    public async Task<DiscoverySnapshot> FindAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var connection = await GetAsync(id, cancellationToken);
        var result = await DiscoverAsync(id, subject, actor, cancellationToken);
        var fields = (await gateway.ListAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, connection.AdapterKey, StringComparison.Ordinal))?.GroupingFields ?? [];
        return snapshots.Add(id, subject.UserId, result, fields);
    }

    /// <summary>What this person found earlier, or an explanation that they need to find again.</summary>
    public DiscoverySnapshot GetSnapshot(Guid connectionId, Guid snapshotId, AccessSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return snapshots.Find(snapshotId, connectionId, subject.UserId)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.SnapshotExpired, FailureKind.Conflict,
                "What was found has expired or was replaced. Find what the connection holds again.");
    }

    public Task DeleteAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default) =>
        repository.DeleteAuthorizedAsync(id, subject, actor, cancellationToken);

    private async Task<(IReadOnlyDictionary<string, string> Settings, int Version)> ValidateSettingsAsync(
        string adapterKey, IReadOnlyDictionary<string, string?>? requested, CancellationToken cancellationToken)
    {
        var settings = (requested ?? new Dictionary<string, string?>())
            .ToDictionary(item => item.Key, item => item.Value?.Trim(), StringComparer.Ordinal);
        var descriptor = (await gateway.ListAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, adapterKey, StringComparison.Ordinal));
        var problems = descriptor is null ? null : await gateway.ValidateAsync(adapterKey, settings, cancellationToken);
        if (descriptor is null || problems is null)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.AdapterUnknown, FailureKind.Validation,
                "Choose an adapter that this installation provides.");
        if (problems.Count > 0)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.SettingsInvalid, FailureKind.Validation,
                string.Join(" ", problems.Select(problem => $"{problem.FieldKey}: {problem.Message}")));
        var kept = settings.Where(item => !string.IsNullOrEmpty(item.Value))
            .ToDictionary(item => item.Key, item => item.Value!, StringComparer.Ordinal);
        return (kept, descriptor.SettingsVersion);
    }

    private static string CleanName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > MaximumNameLength || clean.Any(char.IsControl))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                $"A connection name of 1 to {MaximumNameLength} characters is required.");
        return clean;
    }

    private static string Truncate(string? value, int length) =>
        value is null ? string.Empty : value.Length <= length ? value : value[..length];

    internal static WellSiteAutoPilotException NotFound() => new(ConnectionFailureCodes.NotFound, FailureKind.NotFound,
        "The connection was not found.");
}
