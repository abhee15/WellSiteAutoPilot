using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>A person's view of one logic's schedule: what its revision declares, what is live, and how it got there.</summary>
public sealed record ConfiguredLogicScheduleView(
    Guid ConfiguredLogicId,
    Guid? ActiveRevisionId,
    int Version,
    ConfiguredLogicSchedule? Declared,
    ConfiguredLogicSchedule? Effective,
    ConfiguredLogicScheduleState? Live,
    DateTimeOffset? NextRunAtUtc,
    IReadOnlyList<ConfiguredLogicScheduleChange> Changes)
{
    /// <summary>What the caller reviewed: the active revision plus the number of live changes made against it.</summary>
    public string Token => ConfiguredLogicScheduleControlService.FormatToken(ActiveRevisionId, Version);
}

/// <summary>
/// Pause, resume and change the frequency of a logic's schedule without a new revision. Every change is judged against
/// the schedule the caller reviewed, and is applied through the grant-authorised configuration write.
/// </summary>
public sealed class ConfiguredLogicScheduleControlService(
    IConfiguredLogicRepository configurations,
    IConfiguredLogicScheduleRepository schedules,
    TimeProvider timeProvider)
{
    public const int MinimumCadenceSeconds = 15;
    public const int MaximumCadenceSeconds = 7 * 24 * 60 * 60;
    public const int HistoryLimit = 20;
    private const int MaxReasonLength = 1024;

    public async Task<ConfiguredLogicScheduleView> GetAsync(
        AccessSubject subject, Guid configuredLogicId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var definition = await ReadAsync(subject, configuredLogicId, cancellationToken);
        return await BuildViewAsync(definition, cancellationToken);
    }

    public Task<ConfiguredLogicScheduleView> PauseAsync(
        AccessSubject subject, SecurityActorContext actor, Guid configuredLogicId, string expectedToken, string? reason,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(subject, actor, configuredLogicId, expectedToken, reason,
            ConfiguredLogicScheduleChangeKind.Paused, (effective, _) =>
            {
                if (!effective.Enabled) throw Conflict("SCHEDULE_ALREADY_PAUSED", "This schedule is already paused.");
                return effective with { Enabled = false };
            }, cancellationToken);

    public Task<ConfiguredLogicScheduleView> ResumeAsync(
        AccessSubject subject, SecurityActorContext actor, Guid configuredLogicId, string expectedToken, string? reason,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(subject, actor, configuredLogicId, expectedToken, reason,
            ConfiguredLogicScheduleChangeKind.Resumed, (effective, now) =>
            {
                if (effective.Enabled) throw Conflict("SCHEDULE_NOT_PAUSED", "This schedule is not paused.");
                // Missed occurrences are not made up: the first run is one cadence from now.
                return effective with { Enabled = true, StartAtUtc = now.AddSeconds(effective.CadenceSeconds) };
            }, cancellationToken);

    public Task<ConfiguredLogicScheduleView> ChangeFrequencyAsync(
        AccessSubject subject, SecurityActorContext actor, Guid configuredLogicId, string expectedToken,
        int cadenceSeconds, string? reason, CancellationToken cancellationToken = default)
    {
        if (cadenceSeconds is < MinimumCadenceSeconds or > MaximumCadenceSeconds)
            throw new WellSiteAutoPilotException("SCHEDULE_CADENCE_INVALID", FailureKind.Validation,
                $"The frequency must be between {MinimumCadenceSeconds} seconds and {MaximumCadenceSeconds / 86400} days.");
        return ChangeAsync(subject, actor, configuredLogicId, expectedToken, reason,
            ConfiguredLogicScheduleChangeKind.FrequencyChanged, (effective, now) =>
            {
                if (effective.CadenceSeconds == cadenceSeconds)
                    throw new WellSiteAutoPilotException("SCHEDULE_UNCHANGED", FailureKind.Validation,
                        "The schedule already runs at this frequency.");
                // A paused schedule stays paused; its first run is set when it is resumed.
                return effective with
                {
                    CadenceSeconds = cadenceSeconds,
                    StartAtUtc = effective.Enabled ? now.AddSeconds(cadenceSeconds) : effective.StartAtUtc
                };
            }, cancellationToken);
    }

    private async Task<ConfiguredLogicScheduleView> ChangeAsync(
        AccessSubject subject, SecurityActorContext actor, Guid configuredLogicId, string expectedToken, string? reason,
        ConfiguredLogicScheduleChangeKind kind,
        Func<ConfiguredLogicSchedule, DateTimeOffset, ConfiguredLogicSchedule> derive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        var (reviewedRevisionId, reviewedVersion) = ParseToken(expectedToken);
        var normalizedReason = NormalizeReason(reason);

        var definition = await ReadAsync(subject, configuredLogicId, cancellationToken);
        var state = await schedules.GetAsync(configuredLogicId, cancellationToken);
        var active = ActiveRevision(definition);
        var current = Describe(definition, active, state);
        if (current.ActiveRevisionId != reviewedRevisionId || current.Version != reviewedVersion)
            throw Conflict("SCHEDULE_CHANGED", "The schedule changed. Refresh and review before retrying.");
        if (current.Effective is null)
            throw Conflict("SCHEDULE_NOT_CONFIGURED", "The live revision does not run on a schedule.");

        var now = Truncate(timeProvider.GetUtcNow());
        var next = derive(current.Effective, now);
        await configurations.ApplyScheduleChangeAuthorizedAsync(
            new ConfiguredLogicScheduleChangeRequest(
                configuredLogicId, reviewedRevisionId!.Value, reviewedVersion, kind, next, actor.IdentityName, now,
                normalizedReason),
            subject, actor, cancellationToken);

        return await BuildViewAsync(await ReadAsync(subject, configuredLogicId, cancellationToken), cancellationToken);
    }

    private async Task<ConfiguredLogicScheduleView> BuildViewAsync(
        ConfiguredLogicDefinition definition, CancellationToken cancellationToken)
    {
        var state = await schedules.GetAsync(definition.Id, cancellationToken);
        var changes = await schedules.ListChangesAsync(definition.Id, HistoryLimit, cancellationToken);
        var described = Describe(definition, ActiveRevision(definition), state);
        return described with
        {
            NextRunAtUtc = EffectiveSchedule.NextRunAt(ActiveRevision(definition), state, timeProvider.GetUtcNow()),
            Changes = changes
        };
    }

    private static ConfiguredLogicScheduleView Describe(
        ConfiguredLogicDefinition definition, ConfiguredLogicRevision? active, ConfiguredLogicScheduleState? state)
    {
        var live = active is not null && state is not null && state.BasedOnRevisionId == active.Id ? state : null;
        return new ConfiguredLogicScheduleView(
            definition.Id, definition.ActiveRevisionId, live?.Version ?? 0, active?.Schedule,
            EffectiveSchedule.Resolve(active, live), live, null, []);
    }

    private static ConfiguredLogicRevision? ActiveRevision(ConfiguredLogicDefinition definition) =>
        definition.Revisions.SingleOrDefault(item => item.Id == definition.ActiveRevisionId);

    private async Task<ConfiguredLogicDefinition> ReadAsync(
        AccessSubject subject, Guid configuredLogicId, CancellationToken cancellationToken) =>
        await configurations.GetAuthorizedAsync(subject, configuredLogicId, cancellationToken)
            ?? throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");

    internal static string FormatToken(Guid? activeRevisionId, int version) =>
        $"{activeRevisionId?.ToString("D") ?? "none"}.{version}";

    private static (Guid? RevisionId, int Version) ParseToken(string token)
    {
        var parts = (token ?? string.Empty).Trim('"').Split('.');
        if (parts.Length == 2 && int.TryParse(parts[1], out var version) && version >= 0)
        {
            if (parts[0] == "none") return (null, version);
            if (Guid.TryParseExact(parts[0], "D", out var revisionId)) return (revisionId, version);
        }

        throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
            "The reviewed schedule version is required.");
    }

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > MaxReasonLength)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                $"The reason must be at most {MaxReasonLength} characters.");
        return trimmed;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);

    private static WellSiteAutoPilotException Conflict(string code, string message) =>
        new(code, FailureKind.Conflict, message);
}
