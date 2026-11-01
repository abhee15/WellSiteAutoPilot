using System.Text.Json;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

/// <summary>
/// The single place a live schedule change is staged. Callers own the transaction and the definition lock, so that a
/// system caller and a grant-authorised caller apply exactly the same rules and write exactly the same rows.
/// </summary>
internal static class ConfiguredLogicScheduleWriter
{
    private const int MaxReasonLength = 1024;

    public static async Task<ConfiguredLogicScheduleStateEntity> StageAsync(
        WellSiteAutoPilotDbContext dbContext,
        Guid? activeRevisionId,
        ConfiguredLogicScheduleChangeRequest request,
        SecurityActorContext? actor,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (activeRevisionId != request.ExpectedRevisionId) throw Conflict();

        var current = await dbContext.ConfiguredLogicScheduleStates
            .SingleOrDefaultAsync(item => item.ConfiguredLogicId == request.ConfiguredLogicId, cancellationToken);
        var currentVersion = current is null ? 0 : current.Version;
        if (currentVersion != request.ExpectedVersion) throw Conflict();
        if (current is not null && current.BasedOnRevisionId != request.ExpectedRevisionId) throw Conflict();

        var version = currentVersion + 1;
        if (current is null)
        {
            current = new ConfiguredLogicScheduleStateEntity { ConfiguredLogicId = request.ConfiguredLogicId };
            dbContext.ConfiguredLogicScheduleStates.Add(current);
        }

        current.BasedOnRevisionId = request.ExpectedRevisionId;
        current.Version = version;
        current.Enabled = request.Schedule.Enabled;
        current.CadenceSeconds = request.Schedule.CadenceSeconds;
        current.StartAtUtc = request.Schedule.StartAtUtc;
        current.TimeZoneId = request.Schedule.TimeZoneId;
        current.ChangedBy = request.ChangedBy;
        current.ChangedAtUtc = request.ChangedAtUtc;
        current.Reason = request.Reason;

        dbContext.ConfiguredLogicScheduleChanges.Add(NewChange(
            request.ConfiguredLogicId, request.ExpectedRevisionId, version, request.Kind, request.Schedule,
            request.ChangedBy, request.ChangedAtUtc, request.Reason));
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = request.ChangedAtUtc,
            ActorUserId = actor?.UserId,
            ActorIdentity = actor?.IdentityName ?? "system:configured-logic-schedule",
            Action = AuditAction(request.Kind),
            TargetType = "ConfiguredLogic",
            TargetId = request.ConfiguredLogicId.ToString("D"),
            CorrelationId = actor?.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                revisionId = request.ExpectedRevisionId,
                version,
                enabled = request.Schedule.Enabled,
                cadenceSeconds = request.Schedule.CadenceSeconds
            })
        });
        return current;
    }

    /// <summary>
    /// Activation returns the logic to the schedule its new revision declares: live changes were made against the
    /// previous revision. The discarded state stays visible in the history as a reset.
    /// </summary>
    public static async Task ResetOnActivationAsync(
        WellSiteAutoPilotDbContext dbContext,
        Guid configuredLogicId,
        Guid activatedRevisionId,
        ConfiguredLogicSchedule? declared,
        string changedBy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var removed = await dbContext.ConfiguredLogicScheduleStates
            .Where(item => item.ConfiguredLogicId == configuredLogicId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0 || declared is null) return;
        dbContext.ConfiguredLogicScheduleChanges.Add(NewChange(
            configuredLogicId, activatedRevisionId, 0, ConfiguredLogicScheduleChangeKind.Reset, declared,
            changedBy, nowUtc, "Revision activated"));
    }

    private static ConfiguredLogicScheduleChangeEntity NewChange(
        Guid configuredLogicId, Guid revisionId, int version, ConfiguredLogicScheduleChangeKind kind,
        ConfiguredLogicSchedule schedule, string changedBy, DateTimeOffset changedAtUtc, string? reason) => new()
    {
        Id = Guid.NewGuid(),
        ConfiguredLogicId = configuredLogicId,
        BasedOnRevisionId = revisionId,
        Version = version,
        Kind = kind.ToString(),
        Enabled = schedule.Enabled,
        CadenceSeconds = schedule.CadenceSeconds,
        StartAtUtc = schedule.StartAtUtc,
        TimeZoneId = schedule.TimeZoneId,
        ChangedBy = changedBy,
        ChangedAtUtc = changedAtUtc,
        Reason = reason
    };

    private static string AuditAction(ConfiguredLogicScheduleChangeKind kind) => kind switch
    {
        ConfiguredLogicScheduleChangeKind.Paused => "CONFIGURED_LOGIC_SCHEDULE_PAUSED",
        ConfiguredLogicScheduleChangeKind.Resumed => "CONFIGURED_LOGIC_SCHEDULE_RESUMED",
        ConfiguredLogicScheduleChangeKind.FrequencyChanged => "CONFIGURED_LOGIC_SCHEDULE_FREQUENCY_CHANGED",
        _ => throw Invalid("Reset is recorded by revision activation, not requested directly.")
    };

    private static void Validate(ConfiguredLogicScheduleChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == ConfiguredLogicScheduleChangeKind.Reset)
            throw Invalid("Reset is recorded by revision activation, not requested directly.");
        if (request.Schedule.CadenceSeconds <= 0) throw Invalid("The cadence must be positive.");
        if (string.IsNullOrWhiteSpace(request.Schedule.TimeZoneId)) throw Invalid("A time zone is required.");
        if (request.Schedule.TimeZoneId.Length > 64) throw Invalid("The time zone is too long.");
        if (string.IsNullOrWhiteSpace(request.ChangedBy)) throw Invalid("Who made the change is required.");
        if (request.ChangedBy.Length > 256) throw Invalid("The actor name is too long.");
        if (request.ExpectedVersion < 0) throw Invalid("The expected version cannot be negative.");
        if (request.Reason is { Length: > MaxReasonLength }) throw Invalid("The reason is too long.");
    }

    public static WellSiteAutoPilotException Invalid(string message) =>
        new("SCHEDULE_CHANGE_INVALID", FailureKind.Validation, message);

    public static WellSiteAutoPilotException Conflict() => new(
        "SCHEDULE_CHANGED", FailureKind.Conflict,
        "The schedule or the live revision changed. Refresh and review before retrying.");
}
