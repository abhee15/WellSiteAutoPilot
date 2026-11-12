using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

// Bounded connection/revision sessions. One operation per session coalesces expiry
// and prevents a concurrent 401 token storm. Busy entries are never disposed.
public sealed class ForeSiteConnectionMeasurements(IHttpClientFactory clients, TimeProvider clock) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _sessions = [];
    private bool _disposed;

    public Task<EngineeringValue> ReadAsync(ConnectionMeasurementRequest request, CancellationToken cancellationToken) =>
        WithSessionAsync(request, async session => await new ForeSiteCurrentDataProvider(session.Session)
            .GetCurrentAsync(request.ExternalId, request.Quantity, session.Token), cancellationToken);

    /// <summary>
    /// An internal shared controller snapshot, not a new dynamic or author-accessible provider operation.
    /// It inherits the same connection/revision session, timeout, serialization and error mapping as scalar reads.
    /// </summary>
    public Task<IReadOnlyDictionary<string, EngineeringValue>> ReadSnapshotAsync(
        ConnectionMeasurementRequest request, IReadOnlyCollection<string> quantities, CancellationToken cancellationToken) =>
        WithSessionAsync(request, async session => await new ForeSiteCurrentDataProvider(session.Session)
            .GetCurrentSnapshotAsync(request.ExternalId, quantities, session.Token), cancellationToken);

    private readonly ForeSiteSnapshotCache _snapshots = new(TimeSpan.FromSeconds(10), clock);

    public Task<ResolvePreview> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WithSessionAsync(new(request.ConnectionId, request.ConnectionRevision, request.Settings, request.ExternalId, request.Input), async session =>
        {
            var provider = new ForeSiteResolveProvider(session.Session, _snapshots, $"{request.ConnectionId}/{request.ConnectionRevision}");
            var result = await provider.ResolveAsync(request.ExternalId, request.Input, request.Basis, session.Token);
            if (!request.Read || result.Outcome != ResolveOutcome.Compatible)
                return new ResolvePreview(result, null, null);
            try { return new ResolvePreview(result, await provider.ReadAsync(request.ExternalId, result.Candidates[0], session.Token), null); }
            catch (ForeSiteMeasurementException error) { return new ResolvePreview(result, null, error.Code); }
        }, cancellationToken);
    }

    public Task<SetPointWritePlan> PlanSetPointAsync(SetPointPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WithSessionAsync(new(request.ConnectionId, request.ConnectionRevision, request.Settings, request.ExternalId, request.SetPoint), async session =>
            await new ForeSiteResolveProvider(session.Session)
                .PlanSetPointChangeAsync(request.ExternalId, request.SetPoint, request.Delta, session.Token), cancellationToken, budgetSeconds: 60);
    }

    public Task<HistoricalReadResult> ReadHistoryAsync(ConnectionHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HistoricalReadLimits.Validate(request.Read);
        return WithSessionAsync(new(request.ConnectionId, request.ConnectionRevision, request.Settings,
            request.Read.AssetExternalId, request.Read.Quantity),
            async session => await new ForeSiteHistoricalDataProvider(session.Session).ReadHistoryAsync(request.Read, session.Token), cancellationToken);
    }

    public Task<AssetConfigurationResult> ReadConfigurationAsync(ConnectionConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Read);
        return WithSessionAsync(new(request.ConnectionId, request.ConnectionRevision, request.Settings,
            request.Read.AssetExternalId, "AssetConfiguration"),
            async session => await new ForeSiteConfigurationDataProvider(session.Session).ReadConfigurationAsync(request.Read, session.Token), cancellationToken);
    }

    private async Task<T> WithSessionAsync<T>(ConnectionMeasurementRequest request,
        Func<(ForeSiteSession Session, CancellationToken Token), Task<T>> read, CancellationToken cancellationToken, int budgetSeconds = 6)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(ForeSiteAdapter.Descriptor, request.Settings).Count != 0)
            throw Failure("FORESITE_CONNECTION_INVALID");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));
        var entry = Acquire(request);
        try
        {
            await entry.Gate.WaitAsync(budget.Token);
            try
            {
                return await read((entry.Session, budget.Token));
            }
            finally { entry.Gate.Release(); }
        }
        catch (ForeSiteMeasurementException error) { throw Failure(error.Code); }
        catch (ForeSiteTokenException) { throw Failure("FORESITE_AUTHENTICATION_FAILED"); }
        catch (HttpRequestException) { throw Failure("FORESITE_UNREACHABLE"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Failure("FORESITE_READ_TIMEOUT"); }
        finally
        {
            lock (_gate)
            {
                entry.Users--;
                entry.LastUse = clock.GetUtcNow();
                if (entry.Retired && entry.Users == 0) entry.Dispose();
            }
        }
    }

    private Entry Acquire(ConnectionMeasurementRequest request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var pair in _sessions.Where(pair => pair.Value.Users == 0 &&
                clock.GetUtcNow() - pair.Value.LastUse > TimeSpan.FromMinutes(2)).ToArray())
            {
                _sessions.Remove(pair.Key);
                pair.Value.Dispose();
            }
            var address = ForeSiteAdapter.BaseAddress(request.Settings[ForeSiteAdapter.BaseUrlSetting]!);
            if (_sessions.TryGetValue(request.ConnectionId, out var current) &&
                (current.Revision != request.ConnectionRevision || current.Client.BaseAddress != address))
            {
                _sessions.Remove(request.ConnectionId);
                current.Retired = true;
                if (current.Users == 0) current.Dispose();
                current = null;
            }
            if (current is null)
            {
                if (_sessions.Count >= 128) throw Failure("FORESITE_SESSION_CAPACITY");
                var client = clients.CreateClient(AdapterConnectionTester.WindowsClientName);
                client.BaseAddress = address;
                client.MaxResponseContentBufferSize = 1024 * 1024;
                current = new(client, request.ConnectionRevision, clock.GetUtcNow());
                _sessions.Add(request.ConnectionId, current);
            }
            current.Users++;
            return current;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _sessions.Values)
            {
                entry.Retired = true;
                if (entry.Users == 0) entry.Dispose();
            }
            _sessions.Clear();
        }
    }

    private sealed class Entry(HttpClient client, int revision, DateTimeOffset now) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public int Revision { get; } = revision;
        public ForeSiteSession Session { get; } = new(client);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }
        public bool Retired { get; set; }
        public DateTimeOffset LastUse { get; set; } = now;
        public void Dispose() { Client.Dispose(); Gate.Dispose(); }
    }
    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.DependencyUnavailable,
        "The saved ForeSite connection did not provide the requested data.");
}
