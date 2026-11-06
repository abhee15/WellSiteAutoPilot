namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// Holds the raw <c>Surveillance/well/{id}</c> document for a few seconds so several quantities asked for in one
/// execution are served by one ForeSite call. Callers serialize access per connection, so no locking is needed.
/// </summary>
public sealed class ForeSiteStatusCache(TimeProvider clock, TimeSpan timeToLive)
{
    private const int MaximumWells = 256;
    private readonly Dictionary<long, (DateTimeOffset FetchedAt, byte[] Body)> _items = [];

    public bool TryGet(long wellId, out byte[] body)
    {
        body = [];
        if (timeToLive <= TimeSpan.Zero || !_items.TryGetValue(wellId, out var item)) return false;
        if (clock.GetUtcNow() - item.FetchedAt > timeToLive)
        {
            _items.Remove(wellId);
            return false;
        }
        body = item.Body;
        return true;
    }

    public void Set(long wellId, byte[] body)
    {
        if (timeToLive <= TimeSpan.Zero) return;
        if (_items.Count >= MaximumWells) _items.Clear();
        _items[wellId] = (clock.GetUtcNow(), body);
    }
}
