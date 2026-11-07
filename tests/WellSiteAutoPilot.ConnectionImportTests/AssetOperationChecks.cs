using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class AssetOperationChecks
{
    public static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new ConnectionRecord(Guid.NewGuid(), "Field", "foresite", 1, new Dictionary<string, string>(), true, 4, now, now,
            new(AdapterTestOutcomes.Healthy, "OK", null, now, 4));
        var source = new AssetConnectionSource(Guid.NewGuid(), connection.Id, true, [new(ForeSiteAdapter.WellResourceKind, "101")]);
        var adapter = ForeSiteAdapter.Descriptor;
        void State(AssetOperationEligibilityState expected, ConnectionRecord? c = null, AssetConnectionSource? s = null, AdapterDescriptor? a = null)
        {
            var operations = AssetOperationEligibilityEvaluator.Evaluate(c ?? connection, a ?? adapter, s ?? source);
            Check(operations.Count == adapter.ReadOperations().Count && operations.All(item => item.State == expected), expected.ToString());
        }
        State(AssetOperationEligibilityState.SourceCheckRequired);
        State(AssetOperationEligibilityState.ConnectionDisabled, c: connection with { IsEnabled = false });
        State(AssetOperationEligibilityState.ConnectionIncompatible, c: connection with { SettingsVersion = 2 });
        State(AssetOperationEligibilityState.ConnectionTestRequired, c: connection with { Revision = 5 });
        State(AssetOperationEligibilityState.ConnectionUnavailable, c: connection with { LastTest = connection.LastTest! with { Outcome = AdapterTestOutcomes.Unreachable } });
        State(AssetOperationEligibilityState.AssetInactive, s: source with { IsActive = false });
        State(AssetOperationEligibilityState.Unmapped, s: source with { Bindings = [] });
        State(AssetOperationEligibilityState.Unmapped, s: source with { Bindings = [new(ForeSiteAdapter.WellResourceKind, " ")] });
        State(AssetOperationEligibilityState.AmbiguousSource, s: source with { Bindings = [source.Bindings[0], source.Bindings[0]] });
        State(AssetOperationEligibilityState.ResourceUnsupported, s: source with { Bindings = [new("other.well", "101")] });
        Check(AssetOperationEligibilityEvaluator.Evaluate(connection, null, source).Count == 0, "Missing adapter fell back");
        Check(AssetOperationEligibilityEvaluator.Evaluate(connection, adapter with { Key = "other" }, source).Count == 0, "Another adapter substituted");
        Check(AssetOperationEligibilityEvaluator.Evaluate(connection, adapter, source with { ConnectionId = Guid.NewGuid() }).Count == 0, "Other connection source accepted");

        var subject = new AccessSubject(Guid.NewGuid());
        var repository = new Connections(connection);
        var reader = new Sources(source);
        var access = new Access();
        var gateway = new Gateway(adapter);
        var service = new AssetOperationService(repository, gateway, reader, access);
        Task<AssetOperationsSnapshot> Inspect(string key = "foresite", int revision = 4) => service.InspectAsync(subject, connection.Id, source.AssetId, key, revision);
        var result = await Inspect();
        Check(result.ConnectionRevision == 4 && result.Operations.All(item => item.State == AssetOperationEligibilityState.SourceCheckRequired), "Metadata claims live availability");
        Check(gateway.ListCalls == 1 && reader.Calls == 2 && access.Calls == 2, "Fresh grant/source checks missing");
        await Refused(() => Inspect("cygnet"), FailureKind.Validation);
        await Refused(() => Inspect(revision: 3), FailureKind.Conflict);
        Check(gateway.ListCalls == 1, "Invalid context reached gateway");
        await Refused(() => Inspect("", 0), FailureKind.Validation);
        reader.Current = null;
        await Refused(() => Inspect(), FailureKind.NotFound);
        reader.Current = source with { ConnectionId = Guid.NewGuid() };
        await Refused(() => Inspect(), FailureKind.NotFound);
        reader.Current = source;
        access.Allowed = false;
        await Refused(() => Inspect(), FailureKind.Authorization);
        access.Allowed = true;

        gateway.AfterList = () => access.Allowed = false;
        await Refused(() => Inspect(), FailureKind.Authorization);
        access.Allowed = true;
        gateway.AfterList = () => reader.Current = null;
        await Refused(() => Inspect(), FailureKind.NotFound);
        reader.Current = source;
        gateway.AfterList = () => repository.Current = connection with { Revision = 5 };
        await Refused(() => Inspect(), FailureKind.Conflict);
        repository.Current = connection;
        gateway.AfterList = () => reader.Current = source with { Bindings = [new(ForeSiteAdapter.WellResourceKind, "changed")] };
        await Refused(() => Inspect(), FailureKind.Conflict);
        reader.Current = source;
        gateway.AfterList = null;
        gateway.Descriptors = [adapter with { Key = "other" }];
        Check((await Inspect()).Operations.Count == 0, "Uninstalled adapter fell back");
    }

    private static async Task Refused(Func<Task> action, FailureKind kind)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException("Expected eligibility refusal: " + kind);
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Sources(AssetConnectionSource source) : IAssetConnectionSourceReader
    {
        public AssetConnectionSource? Current = source;
        public int Calls;
        public Task<AssetConnectionSource?> GetConnectionSourceAuthorizedAsync(AccessSubject subject, Guid assetId, Guid connectionId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Current); }
    }
    private sealed class Access : IRequestAccessRepository
    {
        public bool Allowed = true;
        public int Calls;
        public Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default)
        { Check(permission == WellSitePermissions.ConnectionsRead, "Wrong deployment permission"); Calls++; return Task.FromResult(Allowed); }
        public Task<bool> HasAdministratorAsync(AccessSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAllAssetPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Gateway(AdapterDescriptor adapter) : IGatewayAdapters
    {
        public IReadOnlyList<AdapterDescriptor> Descriptors = [adapter];
        public Action? AfterList;
        public int ListCalls;
        public Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        { ListCalls++; AfterList?.Invoke(); return Task.FromResult(Descriptors); }
        public Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(string key, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdapterTestResult?> TestAsync(string key, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DiscoveryResult?> DiscoverAsync(string key, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Connections(ConnectionRecord initial) : IConnectionRepository
    {
        public ConnectionRecord Current = initial;
        public Task<ConnectionRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionRecord?>(id == Current.Id ? Current : null);
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
