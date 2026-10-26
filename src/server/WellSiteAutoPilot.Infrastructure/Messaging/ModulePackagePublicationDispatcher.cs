using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed partial class ModulePackagePublicationDispatcher(IServiceScopeFactory scopeFactory,
    ILogger<ModulePackagePublicationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<ModulePackagePublisher>();
                var completed = await publisher.DispatchBatchAsync(stoppingToken);
                if (completed == 0) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                LogDispatchFailure(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    [LoggerMessage(EventId = 2121, Level = LogLevel.Warning,
        Message = "Module package dispatcher failed; queued work remains pending.")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);
}
