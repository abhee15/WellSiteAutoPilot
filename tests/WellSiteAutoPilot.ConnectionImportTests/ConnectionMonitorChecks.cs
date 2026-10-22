using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>The connection monitor's pacing and recording rules, checked without a database or a Gateway.</summary>
internal static class ConnectionMonitorChecks
{
    public static void Verify()
    {
        var interval = TimeSpan.FromMinutes(5);
        Check(ConnectionMonitorService.DelayAfter(0, interval, 0) == interval, "a healthy connection waits the normal interval");
        Check(ConnectionMonitorService.DelayAfter(1, interval, 0) == TimeSpan.FromMinutes(10), "one failure doubles the wait");
        Check(ConnectionMonitorService.DelayAfter(2, interval, 0) == TimeSpan.FromMinutes(20), "two failures double it twice");
        Check(ConnectionMonitorService.DelayAfter(3, interval, 0) == TimeSpan.FromMinutes(30), "the wait stops at 30 minutes");
        Check(ConnectionMonitorService.DelayAfter(50, interval, 0) == TimeSpan.FromMinutes(30), "many failures never wait longer");
        Check(ConnectionMonitorService.DelayAfter(0, interval, 1) == interval + TimeSpan.FromSeconds(30), "the random spread adds at most a tenth");
        Check(new ConnectionMonitorOptions { IntervalSeconds = 5 }.Interval == TimeSpan.FromSeconds(60), "the interval is never under a minute");

        CheckRecording().GetAwaiter().GetResult();
    }

    private static async Task CheckRecording()
    {
        var connection = new ConnectionRecord(Guid.NewGuid(), "Field", "simulator", 1, new Dictionary<string, string>(), true, 3,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var repository = new Repository(connection);
        var gateway = new Gateway(new AdapterTestResult(AdapterTestOutcomes.Unreachable, "No answer", null));
        var monitor = new ConnectionMonitorService(repository, gateway, TimeProvider.System);

        var changed = await monitor.CheckAsync(connection.Id, TimeSpan.FromMinutes(5), CancellationToken.None);
        Check(changed && repository.Recorded?.Source == ConnectionTestSources.Scheduled, "a check is recorded as scheduled");
        Check(repository.Recorded?.TestedRevision == 3 && repository.Recorded.Outcome == AdapterTestOutcomes.Unreachable,
            "a check records the revision it tested and what it found");
        Check(repository.DelayFor(1) >= TimeSpan.FromMinutes(10) && repository.DelayFor(0) >= interval &&
            repository.DelayFor(0) < TimeSpan.FromMinutes(10), "the next check waits longer after a failure");

        repository.Recorded = null;
        gateway.Throw = true;
        Check(!await monitor.CheckAsync(connection.Id, TimeSpan.FromMinutes(5), CancellationToken.None) && repository.Recorded is null,
            "a Gateway that cannot be asked records nothing");

        gateway.Throw = false;
        repository.Connection = connection with { IsEnabled = false };
        Check(!await monitor.CheckAsync(connection.Id, TimeSpan.FromMinutes(5), CancellationToken.None) && repository.Recorded is null,
            "a disabled connection is not checked");

        repository.Connection = connection;
        gateway.Result = new AdapterTestResult("Surprise", "x", null);
        Check(!await monitor.CheckAsync(connection.Id, TimeSpan.FromMinutes(5), CancellationToken.None) && repository.Recorded is null,
            "an answer the Server does not understand is not recorded");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Connection monitor check failed: " + message);
    }

    private sealed class Gateway(AdapterTestResult result) : IGatewayAdapters
    {
        public AdapterTestResult Result { get; set; } = result;
        public bool Throw { get; set; }

        public Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(string adapterKey,
            IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdapterTestResult?> TestAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings,
            CancellationToken cancellationToken = default) =>
            Throw ? throw new HttpRequestException("Gateway down") : Task.FromResult<AdapterTestResult?>(Result);

        public Task<DiscoveryResult?> DiscoverAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Repository(ConnectionRecord connection) : IConnectionRepository
    {
        private Func<int, TimeSpan>? delay;

        public ConnectionRecord Connection { get; set; } = connection;
        public ConnectionTestRecord? Recorded { get; set; }

        public TimeSpan DelayFor(int failures) => delay!(failures);

        public Task<ConnectionRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionRecord?>(id == Connection.Id ? Connection : null);

        public Task<ScheduledCheckOutcome> RecordScheduledCheckAsync(Guid id, ConnectionTestRecord result,
            Func<int, TimeSpan> delayAfter, CancellationToken cancellationToken = default)
        {
            Recorded = result;
            delay = delayAfter;
            return Task.FromResult(new ScheduledCheckOutcome(true, true));
        }

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
        public Task RecordDiscoveryAuthorizedAsync(Guid id, int found, int warnings, bool complete, AccessSubject subject,
            SecurityActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAuthorizedAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
