using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Scheduling;

public sealed record ScheduleTarget(Guid AssetId, string? Name);

public sealed record ScheduleLastRun(
    Guid ExecutionId,
    DateTimeOffset? ScheduledForUtc,
    ExecutionStatus Status,
    string? ResultCode,
    string? FailureCode);

/// <param name="CadenceSeconds">What the active revision stores.</param>
/// <param name="EffectiveCadenceSeconds">What dispatch uses: the stored cadence, raised to the provider minimum.</param>
/// <param name="MinimumCadenceSeconds">The shortest cadence the providers behind this configuration allow.</param>
public sealed record ScheduleOverviewItem(
    Guid ConfiguredLogicId,
    string Name,
    Guid ActiveRevisionId,
    int RevisionNumber,
    ExecutionMode Mode,
    IReadOnlyList<ScheduleTarget> Targets,
    Guid? ViewId,
    bool Enabled,
    int CadenceSeconds,
    int EffectiveCadenceSeconds,
    int MinimumCadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId,
    DateTimeOffset? NextRunAtUtc,
    bool Running,
    ScheduleLastRun? LastRun,
    bool CanManage);

public sealed record ScheduleChange(int? CadenceSeconds, bool? Enabled, string ExpectedActiveRevisionId);

public sealed record ScheduleListFilter(Guid? AssetId = null, Guid? ViewId = null, Guid? ConfiguredLogicId = null);

public sealed class ScheduleManagementService(
    IConfiguredLogicRepository configurations,
    ConfiguredLogicService configuredLogic,
    IExecutionRepository executions,
    IAssetRepository assets,
    IResourceActionRepository actions,
    IScheduleFloorResolver floor,
    TimeProvider timeProvider)
{
    private const int RecentExecutionWindow = 25;

    private static readonly HashSet<ExecutionStatus> ActiveStatuses =
    [
        ExecutionStatus.Requested, ExecutionStatus.Queued, ExecutionStatus.Starting, ExecutionStatus.Running,
        ExecutionStatus.Waiting
    ];

    public async Task<IReadOnlyList<ScheduleOverviewItem>> ListAsync(AccessSubject subject, ScheduleListFilter filter,
        int limit, CancellationToken cancellationToken = default)
    {
        var all = await configurations.ListAuthorizedAsync(subject, limit, cancellationToken);
        var scheduled = all.Where(item => ActiveScheduledRevision(item) is not null)
            .Where(item => filter.ConfiguredLogicId is null || item.Id == filter.ConfiguredLogicId)
            .Where(item => Matches(ActiveScheduledRevision(item)!, filter))
            .ToArray();
        var manageable = (await actions.GetConfiguredLogicActionsAsync(subject, scheduled.Select(item => item.Id).ToArray(),
            cancellationToken)).ToDictionary(item => item.Id);
        var names = new Dictionary<Guid, string?>();
        var items = new List<ScheduleOverviewItem>(scheduled.Length);
        foreach (var item in scheduled)
            items.Add(await ToItemAsync(item, subject, names, manageable.GetValueOrDefault(item.Id)?.CanManage == true,
                cancellationToken));
        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// A schedule belongs to a revision, and revisions are immutable once validated. A change therefore creates, validates
    /// and activates a new revision that differs only in its schedule, so the audit trail and past executions stay exact.
    /// </summary>
    public async Task<ScheduleOverviewItem> UpdateAsync(Guid configuredLogicId, ScheduleChange change,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.CadenceSeconds is null && change.Enabled is null)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "A cadence or an enabled state is required.");

        var definition = await configurations.GetAuthorizedAsync(subject, configuredLogicId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");
        var active = ActiveScheduledRevision(definition) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_SCHEDULE_NOT_FOUND", FailureKind.Conflict,
                "This configuration has no active schedule to change.");
        if (!string.Equals(change.ExpectedActiveRevisionId?.Trim('"'), active.Id.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_REVISION_CONFLICT", FailureKind.Conflict,
                "The active revision changed. Refresh and review before changing the schedule.");

        var current = active.Schedule!;
        var next = current with
        {
            CadenceSeconds = change.CadenceSeconds ?? current.CadenceSeconds,
            Enabled = change.Enabled ?? current.Enabled
        };

        if (next != current)
        {
            var revision = await configuredLogic.CreateRevisionAuthorizedAsync(configuredLogicId,
                new CreateConfiguredLogicRevisionCommand(
                    active.ModuleManifestJson,
                    active.ParametersJson,
                    active.AssetBindings.Select(item => new ConfiguredLogicAssetBindingCommand(
                        item.Role, item.AssetId, item.ParameterOverridesJson)).ToArray(),
                    active.DataBindings.Select(item => new ConfiguredLogicDataBindingCommand(
                        item.RequirementId, item.AssetId, item.ProviderId, item.ProviderAssetExternalId,
                        item.ProviderMappingJson)).ToArray(),
                    new ConfiguredLogicScheduleCommand(next.Enabled, next.CadenceSeconds, next.StartAtUtc, next.TimeZoneId),
                    active.View is null
                        ? null
                        : new ConfiguredLogicViewBindingCommand(active.View.ViewId, active.View.Revision, active.View.Role),
                    active.Mode),
                subject, actor, cancellationToken);
            await configuredLogic.ValidateRevisionAuthorizedAsync(configuredLogicId, revision.Id, subject, actor,
                cancellationToken);
            await configuredLogic.ActivateRevisionAuthorizedAsync(configuredLogicId, revision.Id,
                active.Id.ToString("D"), subject, actor, cancellationToken);
        }

        var items = await ListAsync(subject, new ScheduleListFilter(ConfiguredLogicId: configuredLogicId), 500,
            cancellationToken);
        return items.SingleOrDefault() ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_SCHEDULE_NOT_FOUND", FailureKind.Conflict,
                "This configuration has no active schedule.");
    }

    private async Task<ScheduleOverviewItem> ToItemAsync(ConfiguredLogicDefinition definition, AccessSubject subject,
        Dictionary<Guid, string?> names, bool canManage, CancellationToken cancellationToken)
    {
        var revision = ActiveScheduledRevision(definition)!;
        var schedule = revision.Schedule!;
        var minimum = await floor.MinimumCadenceSecondsAsync(
            revision.DataBindings.Select(item => item.ProviderId).ToArray(), revision.View?.ConnectionId, cancellationToken);
        var effective = SchedulePolicy.Clamp(schedule, minimum);

        var targets = new List<ScheduleTarget>();
        foreach (var assetId in revision.AssetBindings.Select(item => item.AssetId).Distinct())
        {
            if (!names.TryGetValue(assetId, out var name))
                names[assetId] = name = (await assets.GetAuthorizedAsync(subject, assetId, cancellationToken))?.Name;
            targets.Add(new ScheduleTarget(assetId, name));
        }

        var recent = await executions.ListAuthorizedAsync(subject, null, RecentExecutionWindow, definition.Id,
            cancellationToken: cancellationToken);
        var scheduledRuns = recent.Where(item => item.Trigger == ExecutionTriggerKind.Scheduled).ToArray();
        var last = scheduledRuns.FirstOrDefault();

        return new ScheduleOverviewItem(
            definition.Id,
            definition.Name,
            revision.Id,
            revision.RevisionNumber,
            revision.Mode,
            targets,
            revision.View?.ViewId,
            schedule.Enabled,
            schedule.CadenceSeconds,
            effective.CadenceSeconds,
            minimum,
            schedule.StartAtUtc,
            schedule.TimeZoneId,
            SchedulePolicy.NextOccurrence(effective, timeProvider.GetUtcNow()),
            scheduledRuns.Any(item => ActiveStatuses.Contains(item.Status)),
            last is null ? null : new ScheduleLastRun(last.Id, last.ScheduledForUtc, last.Status, last.ResultCode, last.FailureCode),
            canManage);
    }

    private static ConfiguredLogicRevision? ActiveScheduledRevision(ConfiguredLogicDefinition definition)
    {
        if (definition.ActiveRevisionId is not Guid activeId)
            return null;
        var revision = definition.Revisions.SingleOrDefault(item => item.Id == activeId);
        return revision?.Schedule is null ? null : revision;
    }

    private static bool Matches(ConfiguredLogicRevision revision, ScheduleListFilter filter) =>
        (filter.AssetId is null || revision.AssetBindings.Any(item => item.AssetId == filter.AssetId)) &&
        (filter.ViewId is null || revision.View?.ViewId == filter.ViewId);
}
