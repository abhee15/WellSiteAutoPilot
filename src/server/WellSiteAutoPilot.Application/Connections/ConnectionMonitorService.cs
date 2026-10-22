using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>How the connection monitor paces itself. See the connections workflow design, section 20.</summary>
public sealed class ConnectionMonitorOptions
{
    public const string SectionName = "ConnectionMonitor";
    public const int MinimumIntervalSeconds = 60;
    public const int MaximumBackoffSeconds = 30 * 60;
    public const int MaximumConcurrentChecks = 4;

    /// <summary>Seconds between checks of a healthy connection.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>Turns the monitor off, for installations that want only manual tests.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds, IntervalSeconds));
}

/// <summary>
/// Checks every enabled connection on a schedule, using the same adapter test a person runs. It changes nothing in the
/// outside system and nothing in the Assets.
/// </summary>
public sealed class ConnectionMonitorService(
    IConnectionRepository repository,
    IGatewayAdapters gateway,
    TimeProvider timeProvider)
{
    /// <summary>Covers one check; if this instance dies mid-check, another picks the connection up after this.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private const int BatchSize = 50;

    /// <summary>The wait before the next check: the normal interval, doubling per failure in a row up to 30 minutes.</summary>
    public static TimeSpan DelayAfter(int consecutiveFailures, TimeSpan interval, double jitter)
    {
        var cap = TimeSpan.FromSeconds(ConnectionMonitorOptions.MaximumBackoffSeconds);
        var delay = interval;
        for (var i = 0; i < consecutiveFailures && delay < cap; i++) delay *= 2;
        if (delay > cap) delay = cap;
        // A small random spread keeps connections from all being checked in the same instant.
        return delay + TimeSpan.FromSeconds(delay.TotalSeconds * 0.1 * Math.Clamp(jitter, 0, 1));
    }

    /// <summary>Takes the checks that are due, leasing each so no other Server instance runs it too.</summary>
    public Task<IReadOnlyList<Guid>> ClaimDueAsync(CancellationToken cancellationToken = default) =>
        repository.ClaimDueChecksAsync(timeProvider.GetUtcNow(), Lease, BatchSize, cancellationToken);

    /// <summary>
    /// Checks one claimed connection. Returns true when a person looking would now see something different. Each check
    /// needs its own service instance, because the repository behind it is not safe to share between concurrent checks.
    /// </summary>
    public async Task<bool> CheckAsync(Guid id, TimeSpan interval, CancellationToken cancellationToken)
    {
        var connection = await repository.FindAsync(id, cancellationToken);
        if (connection is null || !connection.IsEnabled) return false;
        var settings = connection.Settings.ToDictionary(item => item.Key, item => (string?)item.Value, StringComparer.Ordinal);
        AdapterTestResult? result;
        try
        {
            result = await gateway.TestAsync(connection.AdapterKey, settings, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The Gateway itself could not be asked. That says nothing about the outside system, so record nothing and
            // let the lease run out; the connection is tried again soon.
            return false;
        }
        if (result is null || !AdapterTestOutcomes.IsKnown(result.Outcome)) return false;
        var record = new ConnectionTestRecord(result.Outcome, Truncate(result.Message, 500), Truncate(result.ActingAs, 256),
            timeProvider.GetUtcNow(), connection.Revision, ConnectionTestSources.Scheduled);
        var outcome = await repository.RecordScheduledCheckAsync(id, record,
            failures => DelayAfter(failures, interval, Random.Shared.NextDouble()), cancellationToken);
        return outcome.Changed;
    }

    private static string Truncate(string? value, int length) =>
        value is null ? string.Empty : value.Length <= length ? value : value[..length];
}
