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

            var decision = await DispatchCandidateAsync(
                configuredLogic,
                nowUtc,
                cancellationToken);
            decisions.Add(decision);
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
            revision.Mode != ExecutionMode.Shadow ||
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
            command = BuildExecutionCommand(
                configuredLogic,
                revision,
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

        var execution = await executionService.RequestConfiguredShadowAsync(
            command,
            BuildCorrelationId(
                configuredLogic.Id,
                revision.Id,
                scheduledForUtc.Value),
            cancellationToken);

        return new ScheduledShadowDispatchDecision(
            configuredLogic.Id,
            revision.Id,
            scheduledForUtc,
            ScheduledShadowDispatchOutcome.Dispatched,
            null,
            execution.Id);
    }

    private static ConfiguredShadowExecutionCommand BuildExecutionCommand(
        ConfiguredLogicDefinition configuredLogic,
        ConfiguredLogicRevision revision,
        DateTimeOffset scheduledForUtc)
    {
        var manifest = ConfiguredLogicService.DeserializeAndValidateManifest(
            revision.ModuleManifestJson);

        if (!string.Equals(
                manifest.ModuleId,
                revision.ModuleId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Version,
                revision.ModuleVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configured Logic revision manifest identity does not match its module identity.");
        }

        var requirements = manifest.DataRequirements.ToDictionary(
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);

        var inputs = revision.DataBindings
            .Select(binding =>
            {
                if (!requirements.TryGetValue(
                        binding.RequirementId,
                        out var requirement))
                {
                    throw new InvalidOperationException(
                        $"Configured Logic data binding '{binding.RequirementId}' is not present in the manifest snapshot.");
                }

                return new ConfiguredExecutionInputCommand(
                    binding.RequirementId,
                    binding.AssetId,
                    binding.ProviderId,
                    binding.ProviderAssetExternalId,
                    requirement.Quantity,
                    requirement.Access,
                    requirement.CanonicalUnit,
                    requirement.MaximumAgeSeconds,
                    requirement.AllowUncertainQuality,
                    binding.ProviderMappingJson);
            })
            .ToArray();

        foreach (var requirement in manifest.DataRequirements)
        {
            if (!inputs.Any(
                    item => string.Equals(
                        item.RequirementId,
                        requirement.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Configured Logic data requirement '{requirement.Id}' has no execution input binding.");
            }
        }

        return new ConfiguredShadowExecutionCommand(
            configuredLogic.Id,
            revision.Id,
            revision.ModuleId,
            revision.ModuleVersion,
            revision.ParametersJson,
            revision.AssetBindings
                .Select(binding => new ConfiguredExecutionAssetCommand(
                    binding.Role,
                    binding.AssetId,
                    binding.ParameterOverridesJson))
                .ToArray(),
            inputs,
            ExecutionTriggerKind.Scheduled,
            scheduledForUtc);
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
