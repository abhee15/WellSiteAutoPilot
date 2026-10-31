// In-memory load probe for ScheduledShadowSchedulerService. No database, NATS or AppHost.
// Repositories are DispatchProxy fakes that count calls and advance a virtual clock by a
// modelled round-trip time per query, so sweep duration and dispatch lag include DB latency.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var rtt = TimeSpan.FromMilliseconds(
    double.Parse(Environment.GetEnvironmentVariable("PROBE_RTT_MS") ?? "1", CultureInfo.InvariantCulture));
Console.WriteLine($"Modelled DB round trip: {rtt.TotalMilliseconds} ms per query; 7 round trips per dispatch write transaction.");

Console.WriteLine("\n## A. Scale (cadence 15 s, poll 5 s, run 10 min, runs complete instantly)");
Console.WriteLine("| N | batch | queries/sweep (last) | CPU ms/sweep | max modelled sweep s | lag p50/p95/max s | configs ever dispatched | dispatched/expected | overrun sweeps |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
foreach (var (n, batch) in new[] { (10, 100), (100, 100), (100, 500), (1000, 100), (1000, 500) })
{
    Print(n, batch, Simulate(new Scenario(n, batch, 15, 5, rtt, TimeSpan.Zero, 600)));
}

Console.WriteLine("\n## B. Overlap (N=10, cadence 15 s, run lasts 40 s)");
{
    var r = Simulate(new Scenario(10, 100, 15, 5, rtt, TimeSpan.FromSeconds(40), 600));
    Console.WriteLine($"dispatched={r.Dispatched} expectedOccurrences={r.Expected} overlapBlockedDecisions={r.Blocked} " +
        $"warningLogsEmitted={r.ReasonLogs} (decisions with a ReasonCode, one per config per 5 s sweep)");
}

Console.WriteLine("\n## C. Pause / missed ticks (N=100, cadence 15 s, process down 60 s..660 s)");
{
    var r = Simulate(new Scenario(100, 500, 15, 5, rtt, TimeSpan.Zero, 720, 60, 660));
    Console.WriteLine($"dispatched={r.Dispatched} expectedOccurrences={r.Expected} firstSweepAfterResumeDispatched={r.ResumeDispatched} " +
        $"maxPerConfigInResumeSweep={r.ResumeMaxPerConfig}");
}

Console.WriteLine("\n## D. Sweep slower than poll (N=1000, batch 500, RTT x5)");
Print(1000, 500, Simulate(new Scenario(1000, 500, 15, 5, rtt * 5, TimeSpan.Zero, 600)));

return 0;

static void Print(int n, int batch, Result r) =>
    Console.WriteLine($"| {n} | {batch} | {r.SteadyQueries} | {r.CpuMsPerSweep:F2} | {r.SweepSeconds:F2} | " +
        $"{r.Lag(0.5):F1}/{r.Lag(0.95):F1}/{r.Lag(1):F1} | {r.ConfigsDispatched} | {r.Dispatched}/{r.Expected} | {r.Overruns} |");

static Result Simulate(Scenario s)
{
    var start = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    var clock = new VirtualClock(start);
    var db = new FakeDb(clock, s.Rtt, s.RunDuration);
    var rng = new Random(42);
    var manifest = Manifest();
    for (var i = 0; i < s.N; i++)
    {
        var id = Guid.NewGuid();
        var revId = Guid.NewGuid();
        var asset = Guid.NewGuid();
        var offset = TimeSpan.FromMilliseconds(rng.Next(0, s.CadenceSeconds * 1000));
        var revision = new ConfiguredLogicRevision(revId, id, 1, "probe.module", "1.0.0", manifest,
            ExecutionMode.Shadow, "{}", ConfiguredLogicRevisionStatus.Active, start, start, start,
            new ConfiguredLogicSchedule(true, s.CadenceSeconds, start - TimeSpan.FromHours(1) + offset, "UTC"),
            [new ConfiguredLogicAssetBinding("well", asset, "{}")],
            [new ConfiguredLogicDataBinding("pump-fillage", asset, "simulator", "W", "{}")]);
        var name = $"cfg-{i:D5}";
        db.Configs.Add(new ConfiguredLogicDefinition(id, name, revId, start, [revision]));
        db.Names[id] = name;
    }

    db.Configs.Sort((a, b) => string.CompareOrdinal(db.Names[a.Id], db.Names[b.Id]));
    var configuredRepo = Fake.Create<IConfiguredLogicRepository>(db.ConfiguredHandlers());
    var moduleRepo = Fake.Create<ILogicModuleCatalogRepository>(db.ModuleHandlers());
    var executionRepo = Fake.Create<IExecutionRepository>(db.ExecutionHandlers());
    var service = new ScheduledShadowSchedulerService(
        configuredRepo, moduleRepo, executionRepo, new ExecutionService(executionRepo, clock), clock);

    var result = new Result();
    var grid = start;
    var end = start + TimeSpan.FromSeconds(s.DurationSeconds);
    var cpu = new List<double>();
    var seenFirstAfterResume = false;
    var lastQueries = 0;
    while (grid < end)
    {
        var offsetSec = (grid - start).TotalSeconds;
        if (s.PauseFromSec is { } from && offsetSec >= from && offsetSec < s.PauseToSec)
        {
            grid = start + TimeSpan.FromSeconds(s.PauseToSec!.Value);
            continue;
        }

        clock.Set(grid);
        var queriesBefore = db.Queries;
        var sw = Stopwatch.StartNew();
        var beforeDispatch = db.Dispatches.Count;
        var sweep = service.DispatchDueAsync(s.Batch).GetAwaiter().GetResult();
        sw.Stop();
        cpu.Add(sw.Elapsed.TotalMilliseconds);
        lastQueries = db.Queries - queriesBefore;
        result.ReasonLogs += sweep.Decisions.Count(d => d.ReasonCode is not null);
        result.Blocked += sweep.Decisions.Count(d => d.Outcome == ScheduledShadowDispatchOutcome.OverlapBlocked);
        if (s.PauseToSec is { } resume && !seenFirstAfterResume && offsetSec >= resume)
        {
            seenFirstAfterResume = true;
            var newOnes = db.Dispatches.Skip(beforeDispatch).ToArray();
            result.ResumeDispatched = newOnes.Length;
            result.ResumeMaxPerConfig = newOnes.GroupBy(x => x.Config).Max(g => g.Count());
        }

        var sweepSeconds = (clock.Now - grid).TotalSeconds;
        result.SweepSeconds = Math.Max(result.SweepSeconds, sweepSeconds);
        var next = grid + TimeSpan.FromSeconds(s.PollSeconds);
        if (clock.Now > next)
        {
            result.Overruns++;
            next = clock.Now;
        }

        grid = next;
    }

    result.SteadyQueries = lastQueries;
    result.CpuMsPerSweep = cpu.Skip(1).DefaultIfEmpty(cpu[0]).Average();
    result.Lags = db.Dispatches.Select(d => d.LagSeconds).OrderBy(x => x).ToArray();
    result.Dispatched = db.Dispatches.Count;
    result.ConfigsDispatched = db.Dispatches.Select(d => d.Config).Distinct().Count();
    // Occurrences that fell inside the run, summed over all configurations (served or not).
    foreach (var cfg in db.Configs)
    {
        var sched = cfg.Revisions.Single().Schedule!;
        var first = (long)Math.Ceiling((start - sched.StartAtUtc).TotalSeconds / s.CadenceSeconds);
        var last = (long)Math.Floor((end - sched.StartAtUtc).TotalSeconds / s.CadenceSeconds);
        result.Expected += (int)Math.Max(0, last - first + 1);
    }

    return result;
}

static string Manifest() => """
{
  "manifestVersion": 1,
  "moduleId": "probe.module",
  "version": "1.0.0",
  "displayName": "Probe",
  "publisher": "Weatherford",
  "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot",
  "entryPoint": "Probe.EntryPoint",
  "minimumSdkVersion": "1.0.0",
  "assetRequirements": [
    { "role": "well", "minimumCount": 1, "maximumCount": 1, "requiredAssetTypeKey": "well", "requiredTraits": [] }
  ],
  "dataRequirements": [
    { "id": "pump-fillage", "assetRole": "well", "quantity": "PumpFillage", "access": "Current",
      "canonicalUnit": "%", "maximumAgeSeconds": 60, "allowUncertainQuality": false }
  ],
  "commandRequirements": []
}
""";

sealed record Scenario(int N, int Batch, int CadenceSeconds, int PollSeconds, TimeSpan Rtt,
    TimeSpan RunDuration, int DurationSeconds, int? PauseFromSec = null, int? PauseToSec = null);

sealed class Result
{
    public int SteadyQueries, Dispatched, Expected, ConfigsDispatched, Overruns, Blocked, ReasonLogs,
        ResumeDispatched, ResumeMaxPerConfig;
    public double CpuMsPerSweep, SweepSeconds;
    public double[] Lags = [];
    public double Lag(double q) => Lags.Length == 0 ? double.NaN : Lags[(int)Math.Min(Lags.Length - 1, Math.Floor(q * Lags.Length))];
}

sealed class VirtualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;
    public void Set(DateTimeOffset value) => Now = value;
    public void Advance(TimeSpan by) => Now += by;
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed record Dispatch(Guid Config, double LagSeconds);

sealed class FakeDb(VirtualClock clock, TimeSpan rtt, TimeSpan runDuration)
{
    public List<ConfiguredLogicDefinition> Configs { get; } = [];
    public Dictionary<Guid, string> Names { get; } = [];
    public List<Dispatch> Dispatches { get; } = [];
    public int Queries;
    private readonly HashSet<(Guid, DateTimeOffset)> occurrences = [];
    private readonly Dictionary<Guid, DateTimeOffset> activeUntil = [];

    private void Query(int roundTrips = 1)
    {
        Queries += roundTrips;
        clock.Advance(rtt * roundTrips);
    }

    public Dictionary<string, Func<object?[], object?>> ConfiguredHandlers() => new()
    {
        // The real repository issues 3 queries (join+take, asset bindings, data bindings) per sweep.
        ["ListActiveScheduledAsync"] = a =>
        {
            Query(3);
            return Task.FromResult<IReadOnlyCollection<ConfiguredLogicDefinition>>(Configs.Take((int)a[0]!).ToArray());
        }
    };

    public Dictionary<string, Func<object?[], object?>> ModuleHandlers() => new()
    {
        ["GetAsync"] = a =>
        {
            Query();
            return Task.FromResult<InstalledLogicModule?>(new InstalledLogicModule(Guid.NewGuid(), (string)a[0]!, (string)a[1]!,
                "Probe", "Weatherford", LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, "{}", new string('a', 64),
                LogicModuleTrustStatus.TrustedPublisher, true, clock.Now));
        }
    };

    public Dictionary<string, Func<object?[], object?>> ExecutionHandlers() => new()
    {
        ["ScheduledOccurrenceExistsAsync"] = a =>
        {
            Query();
            return Task.FromResult(occurrences.Contains(((Guid)a[0]!, (DateTimeOffset)a[1]!)));
        },
        ["HasActiveAssetOverlapAsync"] = a =>
        {
            Query();
            return Task.FromResult(activeUntil.TryGetValue((Guid)a[0]!, out var until) && until > clock.Now);
        },
        ["AddConfiguredRequestedAsync"] = a =>
        {
            // begin, advisory lock, exists, overlap, connection lock, insert+scopes+audit save, commit.
            Query(7);
            var execution = (ExecutionRecord)a[0]!;
            var command = (ConfiguredShadowExecutionCommand)a[1]!;
            occurrences.Add((command.ConfigurationRevisionId, command.ScheduledForUtc!.Value));
            activeUntil[command.ConfiguredLogicId] = clock.Now + runDuration;
            Dispatches.Add(new Dispatch(command.ConfiguredLogicId, (clock.Now - command.ScheduledForUtc.Value).TotalSeconds));
            return Task.FromResult(execution);
        }
    };
}

#pragma warning disable CA1852 // DispatchProxy generates a subclass at runtime.
class Fake : DispatchProxy
{
    private Dictionary<string, Func<object?[], object?>> handlers = [];

    public static T Create<T>(Dictionary<string, Func<object?[], object?>> handlers) where T : class
    {
        var proxy = Create<T, Fake>();
        ((Fake)(object)proxy).handlers = handlers;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is not null && handlers.TryGetValue(targetMethod.Name, out var handler))
            return handler(args ?? []);
        throw new NotSupportedException($"Unexpected call: {targetMethod?.Name}");
    }
}
