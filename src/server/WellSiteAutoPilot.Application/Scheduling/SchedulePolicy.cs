using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Scheduling;

public static class SchedulePolicy
{
    public const int DefaultMinimumCadenceSeconds = 15;

    /// <summary>The most restrictive minimum wins when a configuration reads from several adapters.</summary>
    public static int ResolveMinimum(IEnumerable<int?> declaredByAdapters) =>
        declaredByAdapters.Select(item => Math.Max(1, item ?? DefaultMinimumCadenceSeconds))
            .DefaultIfEmpty(DefaultMinimumCadenceSeconds)
            .Max();

    public static ConfiguredLogicSchedule Clamp(ConfiguredLogicSchedule schedule, int minimumSeconds) =>
        schedule.CadenceSeconds >= minimumSeconds ? schedule : schedule with { CadenceSeconds = minimumSeconds };

    /// <summary>The first occurrence strictly after <paramref name="nowUtc"/>, or null when the schedule is paused.</summary>
    public static DateTimeOffset? NextOccurrence(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0)
            return null;
        var start = schedule.StartAtUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();
        if (now < start)
            return start;
        var cadenceTicks = TimeSpan.FromSeconds(schedule.CadenceSeconds).Ticks;
        var index = (now.UtcTicks - start.UtcTicks) / cadenceTicks;
        return start.AddTicks((index + 1) * cadenceTicks);
    }
}

/// <summary>Answers the shortest cadence the providers behind a configuration allow.</summary>
public interface IScheduleFloorResolver
{
    Task<int> MinimumCadenceSecondsAsync(IReadOnlyCollection<string> providerIds, Guid? viewConnectionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads each adapter's declared minimum from the Gateway catalog. A data binding names a Connection (by id) or an
/// adapter key. If the catalog cannot be reached the platform default applies rather than blocking every save.
/// </summary>
public sealed class AdapterScheduleFloorResolver(IConnectionRepository connections, IGatewayAdapters gateway)
    : IScheduleFloorResolver
{
    public async Task<int> MinimumCadenceSecondsAsync(IReadOnlyCollection<string> providerIds, Guid? viewConnectionId,
        CancellationToken cancellationToken = default)
    {
        var adapterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connectionIds = new HashSet<Guid>();
        foreach (var providerId in providerIds)
        {
            if (Guid.TryParse(providerId, out var id)) connectionIds.Add(id);
            else if (!string.IsNullOrWhiteSpace(providerId)) adapterKeys.Add(providerId.Trim());
        }

        if (viewConnectionId is Guid viewConnection) connectionIds.Add(viewConnection);
        foreach (var id in connectionIds)
        {
            var connection = await connections.FindAsync(id, cancellationToken);
            if (connection is not null) adapterKeys.Add(connection.AdapterKey);
        }

        if (adapterKeys.Count == 0)
            return SchedulePolicy.DefaultMinimumCadenceSeconds;

        try
        {
            var catalog = await gateway.ListAsync(cancellationToken);
            return SchedulePolicy.ResolveMinimum(catalog
                .Where(item => adapterKeys.Contains(item.Key))
                .Select(item => item.MinimumScheduleSeconds));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            return SchedulePolicy.DefaultMinimumCadenceSeconds;
        }
    }
}
