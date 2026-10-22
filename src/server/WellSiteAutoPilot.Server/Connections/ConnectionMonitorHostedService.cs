using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Connections;

namespace WellSiteAutoPilot.Server.Connections;

/// <summary>
/// Runs the connection monitor: every few seconds it takes the connection checks that are due (each leased, so several
/// Server instances share the work) and runs at most four at once. See the connections workflow design, section 20.
/// </summary>
public sealed partial class ConnectionMonitorHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ConnectionMonitorOptions> options,
    IChangeNotifier changes,
    ILogger<ConnectionMonitorHostedService> logger) : BackgroundService
{
    /// <summary>How often the monitor looks for checks that have become due; the check interval itself is in the options.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Guid> claimed;
            await using (var scope = scopeFactory.CreateAsyncScope())
                claimed = await scope.ServiceProvider.GetRequiredService<ConnectionMonitorService>()
                    .ClaimDueAsync(cancellationToken);
            if (claimed.Count == 0) return;

            var interval = options.Value.Interval;
            var changed = 0;
            using var gate = new SemaphoreSlim(ConnectionMonitorOptions.MaximumConcurrentChecks);
            await Task.WhenAll(claimed.Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    // One scope, and so one database context, per check: a context is not safe to share between concurrent checks.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var monitor = scope.ServiceProvider.GetRequiredService<ConnectionMonitorService>();
                    if (await monitor.CheckAsync(id, interval, cancellationToken)) Interlocked.Increment(ref changed);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    LogCheckFailed(logger, id, exception);
                }
                finally
                {
                    gate.Release();
                }
            }));

            // Every result was committed before the check returned.
            if (changed > 0) changes.Notify(ChangeArea.Connections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogSweepFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 2320, Level = LogLevel.Warning,
        Message = "Scheduled check of Connection={ConnectionId} failed and will be tried again.")]
    private static partial void LogCheckFailed(ILogger logger, Guid connectionId, Exception exception);

    [LoggerMessage(EventId = 2321, Level = LogLevel.Error,
        Message = "Connection monitor sweep failed and will be retried.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
