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

    public async Task<EngineeringValue> ReadAsync(ConnectionMeasurementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(ForeSiteAdapter.Descriptor, request.Settings).Count != 0)
            throw Failure("FORESITE_CONNECTION_INVALID");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(6));
        var entry = Acquire(request);
        try
        {
            await entry.Gate.WaitAsync(budget.Token);
            try
            {
                return request.Quantity == ForeSiteCards.Quantity
                    ? await ForeSiteCards.ReadAsync(entry.Session, request.ExternalId, budget.Token)
                    : await ForeSiteMeasurements.ReadAsync(entry.Session, request.ExternalId, request.Quantity, budget.Token);
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
        "The saved ForeSite connection did not provide a verified measurement.");
}
