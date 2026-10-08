using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Scheduling;

namespace WellSiteAutoPilot.Server.Scheduling;

public sealed class ScheduledShadowSchedulerOptions
{
    public const string SectionName = "Scheduler";

    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}

public sealed partial class ScheduledShadowSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ScheduledShadowSchedulerOptions> options,
    ILogger<ScheduledShadowSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(options.Value.PollIntervalSeconds, 1, 60);
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 500);
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(pollSeconds),
            timeProvider);

        await RunSweepAsync(batchSize, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSweepAsync(batchSize, stoppingToken);
        }
    }

    private async Task RunSweepAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scheduler = scope.ServiceProvider
                .GetRequiredService<ScheduledShadowSchedulerService>();

            var result = await scheduler.DispatchDueAsync(
                batchSize,
                cancellationToken);

            var dispatched = result.Decisions.Count(
                item => item.Outcome == ScheduledShadowDispatchOutcome.Dispatched);
            var blocked = result.Decisions.Count(
                item => item.Outcome is
                    ScheduledShadowDispatchOutcome.OverlapBlocked or
                    ScheduledShadowDispatchOutcome.ModuleUnavailable or
                    ScheduledShadowDispatchOutcome.ModuleDisabled or
                    ScheduledShadowDispatchOutcome.InvalidConfiguration);

            if (dispatched > 0 || blocked > 0)
            {
                LogSweepCompleted(
                    logger,
                    result.CandidateCount,
                    dispatched,
                    blocked);
            }

            foreach (var decision in result.Decisions.Where(
                         item => item.ReasonCode is not null))
            {
                LogDecision(
                    logger,
                    decision.ConfiguredLogicId,
                    decision.ConfigurationRevisionId,
                    decision.ScheduledForUtc,
                    decision.Outcome.ToString(),
                    decision.ReasonCode!);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogSweepFailed(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 2310,
        Level = LogLevel.Information,
        Message = "Scheduled Shadow sweep evaluated {CandidateCount} candidates, dispatched {DispatchedCount}, blocked {BlockedCount}.")]
    private static partial void LogSweepCompleted(
        ILogger logger,
        int candidateCount,
        int dispatchedCount,
        int blockedCount);

    [LoggerMessage(
        EventId = 2311,
        Level = LogLevel.Warning,
        Message = "Scheduled Shadow decision for ConfiguredLogic={ConfiguredLogicId}, Revision={RevisionId}, ScheduledForUtc={ScheduledForUtc}: {Outcome} ({ReasonCode}).")]
    private static partial void LogDecision(
        ILogger logger,
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset? scheduledForUtc,
        string outcome,
        string reasonCode);

    [LoggerMessage(
        EventId = 2312,
        Level = LogLevel.Error,
        Message = "Scheduled Shadow sweep failed and will be retried.")]
    private static partial void LogSweepFailed(
        ILogger logger,
        Exception exception);
}
