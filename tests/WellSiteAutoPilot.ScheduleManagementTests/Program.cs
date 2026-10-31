using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.ScheduleManagementTests;

// Schedule management checks. Repositories are stubbed, so no database or Gateway is needed.
var start = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
var clock = new FixedClock(start.AddSeconds(110));

CheckPolicy();
await CheckResolverAsync();
await CheckSaveEnforcementAsync();
await CheckManagementAsync();
await CheckDispatchClampAsync();
Console.WriteLine("Schedule management checks passed.");

void CheckPolicy()
{
    Check(SchedulePolicy.ResolveMinimum([]) == 15, "no adapters means the 15 second default");
    Check(SchedulePolicy.ResolveMinimum([null]) == 15, "an adapter that declares nothing uses the default");
    Check(SchedulePolicy.ResolveMinimum([5]) == 5, "an adapter may lower the minimum");
    Check(SchedulePolicy.ResolveMinimum([null, 60, 30]) == 60, "the most restrictive adapter wins");
    var schedule = new ConfiguredLogicSchedule(true, 10, start, "UTC");
    Check(SchedulePolicy.Clamp(schedule, 15).CadenceSeconds == 15, "a faster cadence is raised to the minimum");
    Check(SchedulePolicy.Clamp(schedule, 5).CadenceSeconds == 10, "a slower cadence is kept");
    Check(SchedulePolicy.NextOccurrence(schedule, start.AddSeconds(-5)) == start, "before the start the next run is the start");
    Check(SchedulePolicy.NextOccurrence(schedule, start.AddSeconds(25)) == start.AddSeconds(30), "the next run is the next grid point");
    Check(SchedulePolicy.NextOccurrence(schedule, start.AddSeconds(30)) == start.AddSeconds(40), "an occurrence that is due is not next");
    Check(SchedulePolicy.NextOccurrence(schedule with { Enabled = false }, start) is null, "a paused schedule has no next run");
}

async Task CheckResolverAsync()
{
    var slow = Guid.NewGuid();
    var connections = Stub.Of<IConnectionRepository>(("FindAsync", args => Task.FromResult<ConnectionRecord?>(
        (Guid)args[0]! == slow
            ? new ConnectionRecord(slow, "Slow", "slow-adapter", 1, new Dictionary<string, string>(), true, 1, start, start, null)
            : null)));
    var unreachable = false;
    var gateway = Stub.Of<IGatewayAdapters>(("ListAsync", _ =>
    {
        if (unreachable) throw new HttpRequestException("down");
        return Task.FromResult<IReadOnlyList<AdapterDescriptor>>(
        [
            Adapter("slow-adapter", 60),
            Adapter("quiet-adapter", null)
        ]);
    }));
    var resolver = new AdapterScheduleFloorResolver(connections, gateway);

    Check(await resolver.MinimumCadenceSecondsAsync([], null) == 15, "no providers: default");
    Check(await resolver.MinimumCadenceSecondsAsync(["quiet-adapter"], null) == 15, "an adapter without a declaration: default");
    Check(await resolver.MinimumCadenceSecondsAsync(["quiet-adapter", slow.ToString()], null) == 60,
        "a data binding that names a Connection uses that adapter's minimum");
    Check(await resolver.MinimumCadenceSecondsAsync([], slow) == 60, "the View's Connection counts too");
    Check(await resolver.MinimumCadenceSecondsAsync(["unknown"], null) == 15, "an unknown provider falls back to the default");
    unreachable = true;
    Check(await resolver.MinimumCadenceSecondsAsync(["slow-adapter"], null) == 15, "an unreachable catalog falls back to the default");
}

async Task CheckSaveEnforcementAsync()
{
    var world = new World(start, 30);
    var defaultService = world.ConfiguredLogic(null);
    await ExpectFailureAsync("CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM",
        () => defaultService.CreateRevisionAuthorizedAsync(world.Id, world.RevisionCommand(10), world.Subject, world.Actor),
        "10 seconds is refused by the default minimum");
    await defaultService.CreateRevisionAuthorizedAsync(world.Id, world.RevisionCommand(15), world.Subject, world.Actor);
    Check(world.Definition.Revisions.Count == 2, "15 seconds meets the default minimum");

    var strict = world.ConfiguredLogic(new FixedFloor(60));
    await ExpectFailureAsync("CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM",
        () => strict.CreateRevisionAuthorizedAsync(world.Id, world.RevisionCommand(30), world.Subject, world.Actor),
        "an adapter-declared minimum is enforced on save");
    await strict.CreateRevisionAuthorizedAsync(world.Id, world.RevisionCommand(60), world.Subject, world.Actor);
    Check(world.Definition.Revisions.Count == 3, "the adapter minimum itself is accepted");
}

async Task CheckManagementAsync()
{
    var world = new World(start, 30);
    var floor = new FixedFloor(15);
    var service = world.Management(floor);
    var original = world.Definition.ActiveRevisionId!.Value.ToString("D");

    var listed = await service.ListAsync(world.Subject, new ScheduleListFilter(), 100);
    Check(listed.Count == 1 && listed[0].CadenceSeconds == 30 && listed[0].Enabled && listed[0].CanManage, "the schedule is listed");
    Check(listed[0].Targets.Single().Name == "Well 1", "targets carry the asset name");
    Check(listed[0].NextRunAtUtc == start.AddSeconds(120), "next run follows the cadence grid");
    Check(!listed[0].Running && listed[0].LastRun is null, "no run yet");
    Check((await service.ListAsync(world.Subject, new ScheduleListFilter(AssetId: Guid.NewGuid()), 100)).Count == 0,
        "the asset filter hides other assets");
    Check((await service.ListAsync(world.Subject, new ScheduleListFilter(AssetId: world.AssetId), 100)).Count == 1,
        "the asset filter keeps its asset");

    await ExpectFailureAsync(FailureCodes.ValidationFailed,
        () => service.UpdateAsync(world.Id, new ScheduleChange(null, null, original), world.Subject, world.Actor),
        "an empty change is refused");
    await ExpectFailureAsync("CONFIGURED_LOGIC_REVISION_CONFLICT",
        () => service.UpdateAsync(world.Id, new ScheduleChange(45, null, Guid.NewGuid().ToString("D")), world.Subject, world.Actor),
        "a stale active revision token is refused");

    var updated = await service.UpdateAsync(world.Id, new ScheduleChange(45, null, original), world.Subject, world.Actor);
    Check(updated.CadenceSeconds == 45 && updated.Enabled && updated.RevisionNumber == 2, "frequency change activates a new revision");
    Check(world.Definition.Revisions.Single(item => item.RevisionNumber == 1).Status == ConfiguredLogicRevisionStatus.Superseded,
        "the previous revision is superseded, not edited");

    var paused = await service.UpdateAsync(world.Id,
        new ScheduleChange(null, false, world.Definition.ActiveRevisionId!.Value.ToString("D")), world.Subject, world.Actor);
    Check(!paused.Enabled && paused.NextRunAtUtc is null && paused.CadenceSeconds == 45, "pause keeps the cadence and clears next run");
    var resumed = await service.UpdateAsync(world.Id,
        new ScheduleChange(null, true, world.Definition.ActiveRevisionId!.Value.ToString("D")), world.Subject, world.Actor);
    Check(resumed.Enabled && resumed.RevisionNumber == 4, "resume activates another revision");

    var count = world.Definition.Revisions.Count;
    await service.UpdateAsync(world.Id, new ScheduleChange(45, true, world.Definition.ActiveRevisionId!.Value.ToString("D")),
        world.Subject, world.Actor);
    Check(world.Definition.Revisions.Count == count, "an unchanged schedule creates no revision");

    floor.Seconds = 60;
    var raised = (await service.ListAsync(world.Subject, new ScheduleListFilter(), 100)).Single();
    Check(raised.CadenceSeconds == 45 && raised.EffectiveCadenceSeconds == 60 && raised.MinimumCadenceSeconds == 60,
        "a raised minimum shows the stored and the effective cadence");
    await ExpectFailureAsync("CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM",
        () => service.UpdateAsync(world.Id, new ScheduleChange(30, null, world.Definition.ActiveRevisionId!.Value.ToString("D")),
            world.Subject, world.Actor),
        "a frequency below the minimum is refused");
    Check(world.Definition.Revisions.Count == count, "a refused change leaves no revision behind");
}

async Task CheckDispatchClampAsync()
{
    // Stored cadence 15 s; the adapter now requires 60 s. At 12:01:50 the 15 s grid is due at 12:01:45, the 60 s grid at 12:01:00.
    var world = new World(start, 15);
    DateTimeOffset? probed = null;
    var executions = Stub.Of<IExecutionRepository>(("ScheduledOccurrenceExistsAsync", args =>
    {
        probed = (DateTimeOffset)args[1]!;
        return Task.FromResult(true);
    }));
    var modules = Stub.Of<ILogicModuleCatalogRepository>(("GetAsync", _ => Task.FromResult<InstalledLogicModule?>(world.Module)));
    async Task<ScheduledShadowDispatchDecision> Sweep(IScheduleFloorResolver? floor)
    {
        var scheduler = new ScheduledShadowSchedulerService(world.Repository, modules, executions,
            new ExecutionService(executions, clock), clock, null, floor);
        return (await scheduler.DispatchDueAsync(10)).Decisions.Single();
    }

    var unclamped = await Sweep(new FixedFloor(15));
    Check(unclamped.ScheduledForUtc == start.AddSeconds(105) && probed == start.AddSeconds(105), "a 15 s minimum keeps the 15 s grid");
    var clamped = await Sweep(new FixedFloor(60));
    Check(clamped.ScheduledForUtc == start.AddSeconds(60) && probed == start.AddSeconds(60), "dispatch never runs faster than the minimum");
    Check(clamped.Outcome == ScheduledShadowDispatchOutcome.AlreadyDispatched, "the stub reports the occurrence already dispatched");
}

AdapterDescriptor Adapter(string key, int? minimum) =>
    new(key, key, "", 1, [], [], null, minimum);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Check failed: " + message);
}

static async Task ExpectFailureAsync(string code, Func<Task> action, string message)
{
    try { await action(); }
    catch (WellSiteAutoPilotException exception) when (exception.Code == code) { return; }
    throw new InvalidOperationException("Check failed: " + message);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FixedFloor(int seconds) : IScheduleFloorResolver
{
    public int Seconds { get; set; } = seconds;

    public Task<int> MinimumCadenceSecondsAsync(IReadOnlyCollection<string> providerIds, Guid? viewConnectionId,
        CancellationToken cancellationToken = default) => Task.FromResult(Seconds);
}

/// <summary>One configuration with a single active scheduled revision, held in memory.</summary>
internal sealed class World
{
    private const string Manifest = """
        {"manifestVersion":1,"moduleId":"weatherford.schedule-ux","version":"1.0.0","displayName":"Schedule UX","publisher":"Weatherford",
         "runtime":"DotNet","executionProfile":"ScheduledOneShot","entryPoint":"Weatherford.ScheduleUx.EntryPoint","minimumSdkVersion":"1.0.0",
         "assetRequirements":[{"role":"well","minimumCount":1,"maximumCount":1,"requiredTraits":[]}],
         "dataRequirements":[],"commandRequirements":[]}
        """;

    private readonly DateTimeOffset now;
    private readonly Asset asset;
    private readonly TimeProvider clock;

    public World(DateTimeOffset start, int cadenceSeconds)
    {
        now = start;
        clock = new FixedClock(start.AddSeconds(110));
        AssetId = Guid.NewGuid();
        asset = new Asset(AssetId, Guid.NewGuid(), "Well 1", null, "{}", true, now);
        Module = new InstalledLogicModule(Guid.NewGuid(), "weatherford.schedule-ux", "1.0.0", "Schedule UX", "Weatherford",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, Manifest, new string('a', 64),
            LogicModuleTrustStatus.TrustedPublisher, true, now);
        Id = Guid.NewGuid();
        var revision = new ConfiguredLogicRevision(Guid.NewGuid(), Id, 1, Module.ModuleId, Module.Version, Manifest,
            ExecutionMode.Shadow, "{}", ConfiguredLogicRevisionStatus.Active, now, now, now,
            new ConfiguredLogicSchedule(true, cadenceSeconds, now, "UTC"),
            [new ConfiguredLogicAssetBinding("well", AssetId, "{}")], []);
        Definition = new ConfiguredLogicDefinition(Id, "Pump fillage observer", revision.Id, now, [revision]);
        Subject = new AccessSubject(Guid.NewGuid());
        Actor = new SecurityActorContext(Subject.UserId, "tester", null);

        Repository = Stub.Of<IConfiguredLogicRepository>(
            ("GetAuthorizedAsync", _ => Task.FromResult<ConfiguredLogicDefinition?>(Definition)),
            ("ListAuthorizedAsync", _ => Task.FromResult<IReadOnlyCollection<ConfiguredLogicDefinition>>([Definition])),
            ("ListActiveScheduledAsync", _ => Task.FromResult<IReadOnlyCollection<ConfiguredLogicDefinition>>([Definition])),
            ("AddRevisionAuthorizedAsync", args =>
            {
                Definition = Definition with { Revisions = [.. Definition.Revisions, (ConfiguredLogicRevision)args[0]!] };
                return Task.CompletedTask;
            }),
            ("SetRevisionValidatedAuthorizedAsync", args =>
            {
                Replace(((ConfiguredLogicRevision)args[0]!).Id, item => item with { Status = ConfiguredLogicRevisionStatus.Validated });
                return Task.CompletedTask;
            }),
            ("ActivateRevisionAuthorizedAsync", args =>
            {
                var target = ((ConfiguredLogicRevision)args[0]!).Id;
                Definition = Definition with
                {
                    ActiveRevisionId = target,
                    Revisions = Definition.Revisions.Select(item => item.Id == target
                        ? item with { Status = ConfiguredLogicRevisionStatus.Active }
                        : item.Status == ConfiguredLogicRevisionStatus.Active
                            ? item with { Status = ConfiguredLogicRevisionStatus.Superseded }
                            : item).ToArray()
                };
                return Task.CompletedTask;
            }));
    }

    public Guid Id { get; }
    public Guid AssetId { get; }
    public InstalledLogicModule Module { get; }
    public ConfiguredLogicDefinition Definition { get; private set; }
    public AccessSubject Subject { get; }
    public SecurityActorContext Actor { get; }
    public IConfiguredLogicRepository Repository { get; }

    public CreateConfiguredLogicRevisionCommand RevisionCommand(int cadenceSeconds) =>
        new(Manifest, "{}", [new ConfiguredLogicAssetBindingCommand("well", AssetId, "{}")], [],
            new ConfiguredLogicScheduleCommand(true, cadenceSeconds, now, "UTC"));

    public ConfiguredLogicService ConfiguredLogic(IScheduleFloorResolver? floor)
    {
        var assets = Stub.Of<IAssetRepository>(
            ("GetAssetsByIdsAsync", _ => Task.FromResult<IReadOnlyCollection<Asset>>([asset])),
            ("ListAssetTypesAsync", _ => Task.FromResult<IReadOnlyCollection<AssetTypeDefinition>>([])));
        var modules = Stub.Of<ILogicModuleCatalogRepository>(("GetAsync", _ => Task.FromResult<InstalledLogicModule?>(Module)));
        return new ConfiguredLogicService(Repository, assets, modules, clock, null, null, floor);
    }

    public ScheduleManagementService Management(IScheduleFloorResolver floor)
    {
        var executions = Stub.Of<IExecutionRepository>(("ListAuthorizedAsync",
            _ => Task.FromResult<IReadOnlyCollection<ExecutionRecord>>([])));
        var assets = Stub.Of<IAssetRepository>(("GetAuthorizedAsync", _ => Task.FromResult<Asset?>(asset)));
        var actions = Stub.Of<IResourceActionRepository>(("GetConfiguredLogicActionsAsync",
            _ => Task.FromResult<IReadOnlyCollection<ConfiguredLogicActions>>([new ConfiguredLogicActions(Id, true, true)])));
        return new ScheduleManagementService(Repository, ConfiguredLogic(floor), executions, assets, actions, floor, clock);
    }

    private void Replace(Guid revisionId, Func<ConfiguredLogicRevision, ConfiguredLogicRevision> change) =>
        Definition = Definition with
        {
            Revisions = Definition.Revisions.Select(item => item.Id == revisionId ? change(item) : item).ToArray()
        };
}

