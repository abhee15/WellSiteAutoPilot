using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;

/// <summary>A live schedule change is a configuration write: it needs the same paired grants and leaves the same audit trail.</summary>
internal static class AuthorizedScheduleChecks
{
    public static async Task<int> RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid id,
        AccessSubject subject, SecurityActorContext actor, Guid manageGrant, Guid readGrant)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var activeRevisionId = (await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, id))?.ActiveRevisionId
            ?? throw new InvalidOperationException("The schedule checks need an active revision.");
        var now = DateTimeOffset.UtcNow;
        var baseline = await AuditCountAsync();

        ConfiguredLogicScheduleChangeRequest Request(
            ConfiguredLogicScheduleChangeKind kind, int expectedVersion, bool enabled, Guid? revisionId = null) =>
            new(id, revisionId ?? activeRevisionId, expectedVersion, kind,
                new ConfiguredLogicSchedule(enabled, 300, now.AddMinutes(5), "UTC"), "spoofed-author", now, "probe");

        foreach (var grant in new[] { manageGrant, readGrant })
        {
            await SetGrantAsync(grant, false);
            try
            {
                await ExpectAsync(() => WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Paused, 0, false)), FailureKind.Authorization);
            }
            finally { await SetGrantAsync(grant, true); }
        }
        await ExpectAsync(() => WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Paused, 0, false), new AccessSubject(Guid.NewGuid())),
            FailureKind.Authorization);
        await ExpectAsync(() => WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Paused, 0, false, Guid.NewGuid())), FailureKind.Conflict);
        if (await new ConfiguredLogicScheduleRepository(setup).GetAsync(id) is not null || await AuditCountAsync() != baseline)
            throw new InvalidOperationException("A refused schedule change left state or an audit entry behind.");

        var paused = await WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Paused, 0, false));
        if (paused.Version != 1 || paused.Schedule.Enabled || paused.ChangedBy != actor.IdentityName)
            throw new InvalidOperationException("The authenticated actor, not the request, must be recorded as the author.");
        await ExpectAsync(() => WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Resumed, 0, true)), FailureKind.Conflict);
        var resumed = await WriteAsync(Request(ConfiguredLogicScheduleChangeKind.Resumed, 1, true));
        var changes = await new ConfiguredLogicScheduleRepository(setup).ListChangesAsync(id, 10);
        if (resumed.Version != 2 || changes.Count != 2 || changes.Any(item => item.ChangedBy != actor.IdentityName) ||
            await AuditCountAsync() != baseline + 2)
            throw new InvalidOperationException("Each successful schedule change must be audited with the actor, once.");

        // Restored below so later checks see the revision as the fixture built it.
        // The service judges every change against the schedule the caller reviewed, then applies it through the write above.
        await setup.ConfiguredLogicRevisions.Where(item => item.Id == activeRevisionId).ExecuteUpdateAsync(set => set.SetProperty(
            item => item.ScheduleJson, JsonSerializer.Serialize(new ConfiguredLogicSchedule(true, 300, now.AddHours(-1), "UTC"))));
        var viewed = await ServiceAsync(service => service.GetAsync(subject, id));
        if (viewed.Version != 2 || viewed.Declared is null || viewed.Live is null || viewed.Token != $"{activeRevisionId:D}.2")
            throw new InvalidOperationException("The view must report the declared schedule, the live state and a version token.");
        foreach (var badToken in new[] { "", "garbage", "none.x", $"{activeRevisionId:D}.-1" })
            await ExpectAsync(() => ServiceAsync(service => service.PauseAsync(subject, actor, id, badToken, null)), FailureKind.Validation);
        await ExpectAsync(() => ServiceAsync(service => service.PauseAsync(subject, actor, id, $"{activeRevisionId:D}.1", null)), FailureKind.Conflict);
        await ExpectAsync(() => ServiceAsync(service => service.ResumeAsync(subject, actor, id, viewed.Token, null)), FailureKind.Conflict);

        var servicePaused = await ServiceAsync(service => service.PauseAsync(subject, actor, id, viewed.Token, "  sensor swap  "));
        if (servicePaused.Effective?.Enabled != false || servicePaused.NextRunAtUtc is not null || servicePaused.Version != 3 ||
            servicePaused.Changes[0].Kind != ConfiguredLogicScheduleChangeKind.Paused || servicePaused.Changes[0].Reason != "sensor swap")
            throw new InvalidOperationException("Pausing must stop the schedule, show no next run and record a trimmed reason.");
        await ExpectAsync(() => ServiceAsync(service => service.PauseAsync(subject, actor, id, servicePaused.Token, null)), FailureKind.Conflict);
        await ExpectAsync(() => ServiceAsync(service => service.ChangeFrequencyAsync(subject, actor, id, servicePaused.Token, 5, null)), FailureKind.Validation);
        var retimed = await ServiceAsync(service => service.ChangeFrequencyAsync(subject, actor, id, servicePaused.Token, 600, null));
        if (retimed.Effective?.Enabled != false || retimed.Effective!.CadenceSeconds != 600 ||
            retimed.Effective.StartAtUtc != servicePaused.Effective!.StartAtUtc)
            throw new InvalidOperationException("Changing the frequency of a paused schedule must keep it paused.");
        var serviceResumed = await ServiceAsync(service => service.ResumeAsync(subject, actor, id, retimed.Token, null));
        if (serviceResumed.Effective?.Enabled != true || serviceResumed.NextRunAtUtc != serviceResumed.Effective!.StartAtUtc ||
            serviceResumed.Effective.StartAtUtc <= DateTimeOffset.UtcNow.AddSeconds(500) || !serviceResumed.Declared!.Enabled ||
            serviceResumed.Declared.CadenceSeconds != 300)
            throw new InvalidOperationException("Resuming must start one cadence from now and leave the declared schedule untouched.");
        await ExpectAsync(() => ServiceAsync(service => service.ChangeFrequencyAsync(subject, actor, id, serviceResumed.Token, 600, null)), FailureKind.Validation);
        await setup.ConfiguredLogicRevisions.Where(item => item.Id == activeRevisionId).ExecuteUpdateAsync(set => set.SetProperty(
            item => item.ScheduleJson, (string?)null));
        if (await AuditCountAsync() != baseline + 5)
            throw new InvalidOperationException("Only successful service changes may be audited.");
        Console.WriteLine("Authorized schedule changes require paired grants, record the actor, are judged against the reviewed version and audit once.");
        return 5;

        Task<int> AuditCountAsync() => setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId &&
            item.CorrelationId == actor.CorrelationId && item.Action.StartsWith("CONFIGURED_LOGIC_SCHEDULE_"));
        Task<int> SetGrantAsync(Guid grant, bool active) => setup.AccessGrants.Where(item => item.Id == grant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, active));
        async Task<ConfiguredLogicScheduleView> ServiceAsync(Func<ConfiguredLogicScheduleControlService, Task<ConfiguredLogicScheduleView>> operation)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await operation(new ConfiguredLogicScheduleControlService(
                new ConfiguredLogicRepository(db), new ConfiguredLogicScheduleRepository(db), TimeProvider.System));
        }
        async Task<ConfiguredLogicScheduleState> WriteAsync(ConfiguredLogicScheduleChangeRequest request, AccessSubject? asSubject = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new ConfiguredLogicRepository(db).ApplyScheduleChangeAuthorizedAsync(request, asSubject ?? subject, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected schedule refusal: {kind}.");
    }
}
