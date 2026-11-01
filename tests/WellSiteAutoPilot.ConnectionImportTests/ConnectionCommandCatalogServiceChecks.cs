using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>The catalog service judges a catalog against the adapter before anything is saved, without a database or Gateway.</summary>
internal static class ConnectionCommandCatalogServiceChecks
{
    private static readonly AdapterSettingField Setpoint =
        new("setpoint", "Setpoint name", "Which setpoint.", AdapterSettingKind.Text, true, 16);

    private static readonly AdapterDescriptor Adapter = new("sim", "Simulator", "Simulator", 1, [], [], null, 0,
    [
        new AdapterCommandDefinition("SET_SPEED", "Speed", "Sets the speed.", "Speed", "Hz", AdapterCommandValueKind.Number,
            [Setpoint], AdapterCommandVerification.ReadBack, "Speed", 5, 60, 30, 300)
    ]);

    public static void Verify() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var connection = new ConnectionRecord(Guid.NewGuid(), "Field", "sim", 1, new Dictionary<string, string>(), true, 1,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var catalogs = new Catalogs();
        var service = new ConnectionCommandCatalogService(catalogs, new Connections(connection), new Gateway(Adapter));
        var subject = new AccessSubject(Guid.NewGuid());
        var actor = new SecurityActorContext(subject.UserId, "tester", "c");

        var empty = await service.GetAsync(connection.Id);
        Check(empty.Version == 0 && empty.Entries.Count == 0 && empty.Available.Count == 1,
            "a connection with no catalog has version 0, no entries and the adapter's commands to choose from");

        ConnectionCommandCatalogEntry Entry(string command, decimal? max = 60) =>
            new(command, true, 30, max, 5, new Dictionary<string, string> { ["setpoint"] = "SP1" }, 5, 30);

        await ExpectAsync(() => service.GetAsync(Guid.NewGuid()), FailureKind.NotFound, "an unknown connection is not found");
        await ExpectAsync(() => service.ReplaceAsync(Guid.NewGuid(), 0, [], null, subject, actor), FailureKind.NotFound,
            "saving for an unknown connection is not found");
        await ExpectAsync(() => service.ReplaceAsync(connection.Id, 0, null, null, subject, actor), FailureKind.Validation,
            "a missing catalog is refused");
        await ExpectAsync(() => service.ReplaceAsync(connection.Id, 0, [Entry("NOT_A_COMMAND")], null, subject, actor),
            FailureKind.Validation, "a command the adapter does not define is refused");
        await ExpectAsync(() => service.ReplaceAsync(connection.Id, 0, [Entry("SET_SPEED") with { Minimum = null }], null, subject, actor),
            FailureKind.Validation, "an enabled command without limits is refused");
        Check(catalogs.Saved == 0, "a refused catalog never reaches the repository");

        var saved = await service.ReplaceAsync(connection.Id, 0, [Entry("set_speed")], "go", subject, actor);
        Check(catalogs.Saved == 1 && saved.Version == 1 && saved.Entries[0].Command == "SET_SPEED",
            "a valid catalog is saved under the adapter's own spelling of the command");
        Check(catalogs.LastExpectedVersion == 0, "the reviewed version is passed through");

        var outsider = new ConnectionRecord(Guid.NewGuid(), "Other", "gone", 1, new Dictionary<string, string>(), true, 1,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var orphan = new ConnectionCommandCatalogService(catalogs, new Connections(outsider), new Gateway(Adapter));
        await ExpectAsync(() => orphan.ReplaceAsync(outsider.Id, 0, [], null, subject, actor), FailureKind.Validation,
            "a connection whose adapter the Gateway does not know cannot have its catalog judged");
        Check((await orphan.GetAsync(outsider.Id)).Available.Count == 0, "reading such a catalog still works, with nothing to choose from");
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind, string message)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException("Catalog service check failed: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Catalog service check failed: " + message);
    }

    private sealed class Catalogs : IConnectionCommandCatalogRepository
    {
        private ConnectionCommandCatalog? stored;
        public int Saved { get; private set; }
        public int LastExpectedVersion { get; private set; } = -1;

        public Task<ConnectionCommandCatalog?> FindAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(stored);

        public Task<ConnectionCommandCatalog> ReplaceAuthorizedAsync(Guid connectionId, int expectedVersion,
            IReadOnlyCollection<ConnectionCommandCatalogEntry> entries, string? reason, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default)
        {
            Saved++;
            LastExpectedVersion = expectedVersion;
            stored = new ConnectionCommandCatalog(connectionId, expectedVersion + 1, actor.IdentityName, DateTimeOffset.UnixEpoch,
                reason, entries.ToArray());
            return Task.FromResult(stored);
        }
    }

    private sealed class Gateway(AdapterDescriptor adapter) : IGatewayAdapters
    {
        public Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdapterDescriptor>>([adapter]);
        public Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(string adapterKey,
            IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdapterTestResult?> TestAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DiscoveryResult?> DiscoverAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Connections(ConnectionRecord connection) : IConnectionRepository
    {
        public Task<ConnectionRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionRecord?>(id == connection.Id ? connection : null);
        public Task<IReadOnlyList<ConnectionRecord>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAuthorizedAsync(ConnectionRecord connection, AccessSubject subject, SecurityActorContext actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionRecord> UpdateAuthorizedAsync(Guid id, int expectedRevision, string name, int settingsVersion,
            IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> sourceSettingKeys, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionRecord> SetEnabledAuthorizedAsync(Guid id, int expectedRevision, bool enabled, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionRecord> RecordTestAuthorizedAsync(Guid id, ConnectionTestRecord result, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ClaimDueChecksAsync(DateTimeOffset now, TimeSpan lease, int limit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ScheduledCheckOutcome> RecordScheduledCheckAsync(Guid id, ConnectionTestRecord result,
            Func<int, TimeSpan> delayAfter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordDiscoveryAuthorizedAsync(Guid id, int found, int warnings, bool complete, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAuthorizedAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
