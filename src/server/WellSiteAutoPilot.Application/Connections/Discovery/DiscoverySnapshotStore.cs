using System.Collections.Concurrent;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Discovery;

/// <summary>What one person found in one connection, held so browsing, review and import all see the same list.</summary>
public sealed class DiscoverySnapshot(
    Guid id, Guid connectionId, Guid userId, DateTimeOffset createdAtUtc, DiscoveryResult result,
    IReadOnlyList<DiscoveryField> fields, int? connectionRevision = null)
{
    public Guid Id { get; } = id;

    public int? ConnectionRevision { get; } = connectionRevision;

    public Guid ConnectionId { get; } = connectionId;

    public Guid UserId { get; } = userId;

    public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;

    public DiscoveryResult Result { get; } = result;

    /// <summary>The grouping facts the adapter offered when this was found.</summary>
    public IReadOnlyList<DiscoveryField> Fields { get; } = fields;
}

/// <summary>
/// Holds discoveries in memory for a short time. Never written to storage: it is provider data that is only
/// useful for the review in progress. One snapshot per person and connection; a new find replaces the old one.
/// </summary>
public sealed class DiscoverySnapshotStore(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public const int MaximumSnapshots = 20;

    private readonly ConcurrentDictionary<(Guid ConnectionId, Guid UserId), DiscoverySnapshot> _snapshots = new();

    public DiscoverySnapshot Add(Guid connectionId, Guid userId, DiscoveryResult result, IReadOnlyList<DiscoveryField> fields, int? connectionRevision = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fields);
        var now = timeProvider.GetUtcNow();
        var snapshot = new DiscoverySnapshot(Guid.NewGuid(), connectionId, userId, now, result, fields, connectionRevision);
        _snapshots[(connectionId, userId)] = snapshot;
        Trim(now);
        return snapshot;
    }

    /// <summary>Null when it expired, was replaced, or belongs to another person or connection.</summary>
    public DiscoverySnapshot? Find(Guid snapshotId, Guid connectionId, Guid userId)
    {
        if (!_snapshots.TryGetValue((connectionId, userId), out var snapshot) || snapshot.Id != snapshotId) return null;
        return timeProvider.GetUtcNow() - snapshot.CreatedAtUtc > Lifetime ? null : snapshot;
    }

    private void Trim(DateTimeOffset now)
    {
        foreach (var (key, snapshot) in _snapshots)
            if (now - snapshot.CreatedAtUtc > Lifetime) _snapshots.TryRemove(key, out _);
        while (_snapshots.Count > MaximumSnapshots)
        {
            var oldest = _snapshots.OrderBy(pair => pair.Value.CreatedAtUtc).First();
            _snapshots.TryRemove(oldest.Key, out _);
        }
    }
}
