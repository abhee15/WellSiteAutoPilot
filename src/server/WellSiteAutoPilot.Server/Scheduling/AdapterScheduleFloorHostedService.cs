using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Scheduling;

namespace WellSiteAutoPilot.Server.Scheduling;

/// <summary>
/// Keeps <see cref="AdapterScheduleFloorCache"/> current from the Gateway's adapter catalogue. It retries quickly until the
/// first answer and then refreshes slowly; a failed refresh keeps the last answer, because adapter floors change rarely.
/// </summary>
public sealed partial class AdapterScheduleFloorHostedService(
    IServiceScopeFactory scopeFactory,
    AdapterScheduleFloorCache cache,
    TimeProvider timeProvider,
    ILogger<AdapterScheduleFloorHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = RetryInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                cache.Replace(await scope.ServiceProvider.GetRequiredService<ConnectionService>().ListAdaptersAsync(stoppingToken));
                delay = RefreshInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogRefreshFailed(logger, exception);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The adapter catalogue could not be read; schedule floors keep their last known values.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
