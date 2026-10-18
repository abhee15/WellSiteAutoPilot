using Microsoft.Extensions.Options;

namespace WellSiteAutoPilot.Server.Changes;

/// <summary>
/// Re-checks every live connection's area permissions on a timer and closes any connection
/// that no longer holds all of the areas it was admitted to. The client reconnects, which
/// runs the full check again and admits it only to what it may now read. A failed check
/// closes the connection too: access is never assumed.
/// </summary>
public sealed partial class ChangeConnectionMonitor(ChangeConnectionRegistry registry,
    IServiceScopeFactory scopeFactory, IOptions<ChangeSignalOptions> options, TimeProvider timeProvider,
    ILogger<ChangeConnectionMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RecheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RecheckAsync(stoppingToken);
    }

    /// <summary>Runs one sweep over the connections open now. Public for tests.</summary>
    public async Task RecheckAsync(CancellationToken cancellationToken)
    {
        foreach (var connection in registry.Snapshot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var access = scope.ServiceProvider.GetRequiredService<IChangeAreaAccess>();
                var current = await access.GetReadableAreasAsync(connection.User, cancellationToken);
                if (connection.Areas.All(current.Contains)) continue;
                LogPermissionLost(logger, connection.ConnectionId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogRecheckFailed(logger, exception, connection.ConnectionId);
            }

            connection.Close();
        }
    }

    [LoggerMessage(EventId = 2402, Level = LogLevel.Information,
        Message = "Closing live-update connection {ConnectionId}: an area read permission is no longer held.")]
    private static partial void LogPermissionLost(ILogger logger, string connectionId);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning,
        Message = "Closing live-update connection {ConnectionId}: its permissions could not be re-checked.")]
    private static partial void LogRecheckFailed(ILogger logger, Exception exception, string connectionId);
}
