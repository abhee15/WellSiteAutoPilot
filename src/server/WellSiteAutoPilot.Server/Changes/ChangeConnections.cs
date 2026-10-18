using System.Collections.Concurrent;
using System.Security.Claims;
using WellSiteAutoPilot.Application.Changes;

namespace WellSiteAutoPilot.Server.Changes;

/// <summary>One live hub connection and the areas it was admitted to.</summary>
public sealed class ChangeConnection
{
    private readonly Action close;

    public ChangeConnection(string connectionId, ClaimsPrincipal user, IReadOnlySet<ChangeArea> areas, Action close)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(close);
        ConnectionId = connectionId;
        User = user;
        Areas = areas;
        this.close = close;
    }

    public string ConnectionId { get; }
    public ClaimsPrincipal User { get; }
    public IReadOnlySet<ChangeArea> Areas { get; }

    public void Close() => close();
}

public sealed class ChangeConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ChangeConnection> connections = new(StringComparer.Ordinal);

    public int Count => connections.Count;

    public void Add(ChangeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connections[connection.ConnectionId] = connection;
    }

    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);

    public IReadOnlyCollection<ChangeConnection> Snapshot() => connections.Values.ToArray();
}
