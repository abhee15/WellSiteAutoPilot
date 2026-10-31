using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Scheduling;

public sealed class ScheduledShadowSchedulerService(
    IConfiguredLogicRepository configuredLogicRepository,
    ILogicModuleCatalogRepository moduleCatalogRepository,
    IExecutionRepository executionRepository,
    ExecutionService executionService,
    TimeProvider timeProvider)
{
    public async Task<ScheduledShadowSweepResult> DispatchDueAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Scheduled Shadow batch size must be between 1 and 500.");
        }

        var nowUtc = NormalizeUtcTimestamp(timeProvider.GetUtcNow());
        var candidates = await configuredLogicRepository.ListActiveScheduledAsync(
            limit,
            cancellationToken);
        var decisions = new List<ScheduledShadowDispatchDecision>(candidates.Count);

        foreach (var configuredLogic in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var decision = await DispatchCandidateAsync(
                    configuredLogic,
                    nowUtc,
                    cancellationToken);
                decisions.Add(decision);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Keep processing independent configured logic. No occurrence
                // is marked as dispatched on this path; durable occurrence
                // deduplication handles retries after ambiguous write failures.
                var activeRevision = configuredLogic.ActiveRevisionId is Guid activeRevisionId
                    ? configuredLogic.Revisions.FirstOrDefault(item => item.Id == activeRevisionId)
                    : null;
                decisions.Add(Decision(
                    configuredLogic,
                    activeRevision,
                    activeRevision?.Schedule is { } schedule
                        ? GetLatestDueOccurrence(schedule, nowUtc)
                        : null,
                    ScheduledShadowDispatchOutcome.DispatchFailed,
                    "SCHEDULER_DISPATCH_FAILED"));
            }
        }

        return new ScheduledShadowSweepResult(
            candidates.Count,
            decisions);
    }

    private async Task<ScheduledShadowDispatchDecision> DispatchCandidateAsync(
        ConfiguredLogicDefinition configuredLogic,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var revision = configuredLogic.ActiveRevisionId is Guid activeRevisionId
            ? configuredLogic.Revisions.SingleOrDefault(item => item.Id == activeRevisionId)
            : null;

        if (revision is null ||
            revision.Status != ConfiguredLogicRevisionStatus.Active ||
            revision.Schedule is null)
        {
            return Decision(
                configuredLogic,
                revision,
                null,
                ScheduledShadowDispatchOutcome.InvalidConfiguration,
                "SCHEDULER_ACTIVE_REVISION_INVALID");
        }

        var scheduledForUtc = GetLatestDueOccurrence(
            revision.Schedule,
            nowUtc);

        if (scheduledForUtc is null)
        {
            return Decision(
                configuredLogic,
                revision,
                null,
                ScheduledShadowDispatchOutcome.NotDue,
                null);
        }

        var installedModule = await moduleCatalogRepository.GetAsync(
            revision.ModuleId,
            revision.ModuleVersion,
            cancellationToken);

        if (installedModule is null)
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.ModuleUnavailable,
                "SCHEDULER_MODULE_NOT_INSTALLED");
        }

        if (!installedModule.IsEnabled)
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.ModuleDisabled,
                "SCHEDULER_MODULE_NOT_ENABLED");
        }

        if (await executionRepository.ScheduledOccurrenceExistsAsync(
                revision.Id,
                scheduledForUtc.Value,
                cancellationToken))
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.AlreadyDispatched,
                null);
        }

        var assetIds = revision.AssetBindings
            .Select(item => item.AssetId)
            .Distinct()
            .ToArray();

        if (assetIds.Length == 0)
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.InvalidConfiguration,
                "SCHEDULER_ASSET_SCOPE_EMPTY");
        }

        if (await executionRepository.HasActiveAssetOverlapAsync(
                configuredLogic.Id,
                assetIds,
                cancellationToken))
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.OverlapBlocked,
                "SCHEDULER_EXECUTION_OVERLAP");
        }

        ConfiguredShadowExecutionCommand command;

        try
        {
            command = ConfiguredExecutionSnapshot.Create(
                configuredLogic,
                revision,
                ExecutionTriggerKind.Scheduled,
                scheduledForUtc.Value);
        }
        catch (WellSiteAutoPilotException)
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.InvalidConfiguration,
                "SCHEDULER_CONFIGURATION_INVALID");
        }
        catch (InvalidOperationException)
        {
            return Decision(
                configuredLogic,
                revision,
                scheduledForUtc,
                ScheduledShadowDispatchOutcome.InvalidConfiguration,
                "SCHEDULER_CONFIGURATION_INVALID");
        }

        ExecutionRecord execution;
        try
        {
            execution = await executionService.RequestConfiguredShadowAsync(
                command,
                BuildCorrelationId(configuredLogic.Id, revision.Id, scheduledForUtc.Value),
                cancellationToken);
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_NOT_ENABLED")
        {
            return Decision(configuredLogic, revision, scheduledForUtc,
                ScheduledShadowDispatchOutcome.ModuleDisabled, exception.Code);
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code is
            "EXECUTION_SCHEDULED_OCCURRENCE_EXISTS" or "EXECUTION_ASSET_OVERLAP")
        {
            return Decision(configuredLogic, revision, scheduledForUtc,
                exception.Code == "EXECUTION_SCHEDULED_OCCURRENCE_EXISTS"
                    ? ScheduledShadowDispatchOutcome.AlreadyDispatched : ScheduledShadowDispatchOutcome.OverlapBlocked,
                exception.Code);
        }

        return new ScheduledShadowDispatchDecision(
            configuredLogic.Id,
            revision.Id,
            scheduledForUtc,
            ScheduledShadowDispatchOutcome.Dispatched,
            null,
            execution.Id);
    }

    internal static DateTimeOffset? GetLatestDueOccurrence(
        ConfiguredLogicSchedule schedule,
        DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0)
        {
            return null;
        }

        var startUtc = NormalizeUtcTimestamp(schedule.StartAtUtc);
        var normalizedNowUtc = NormalizeUtcTimestamp(nowUtc);

        if (normalizedNowUtc < startUtc)
        {
            return null;
        }

        TimeSpan cadence;
        try
        {
            cadence = TimeSpan.FromSeconds(schedule.CadenceSeconds);
        }
        catch (OverflowException)
        {
            return null;
        }

        var occurrenceIndex =
            (normalizedNowUtc.UtcTicks - startUtc.UtcTicks) /
            cadence.Ticks;

        return NormalizeUtcTimestamp(
            startUtc.AddTicks(occurrenceIndex * cadence.Ticks));
    }

    private static string BuildCorrelationId(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset scheduledForUtc) =>
        $"scheduler:{configuredLogicId:N}:{revisionId:N}:{scheduledForUtc:yyyyMMddTHHmmss.ffffffZ}";

    private static ScheduledShadowDispatchDecision Decision(
        ConfiguredLogicDefinition configuredLogic,
        ConfiguredLogicRevision? revision,
        DateTimeOffset? scheduledForUtc,
        ScheduledShadowDispatchOutcome outcome,
        string? reasonCode) =>
        new(
            configuredLogic.Id,
            revision?.Id ?? Guid.Empty,
            scheduledForUtc,
            outcome,
            reasonCode,
            null);

    private static DateTimeOffset NormalizeUtcTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
