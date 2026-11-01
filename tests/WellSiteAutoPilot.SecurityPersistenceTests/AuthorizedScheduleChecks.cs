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
        Console.WriteLine("Authorized schedule changes require paired grants, record the actor and audit once.");
        return 2;

        Task<int> AuditCountAsync() => setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId &&
            item.CorrelationId == actor.CorrelationId && item.Action.StartsWith("CONFIGURED_LOGIC_SCHEDULE_"));
        Task<int> SetGrantAsync(Guid grant, bool active) => setup.AccessGrants.Where(item => item.Id == grant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, active));
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
