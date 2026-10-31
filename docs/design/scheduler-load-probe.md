# Scheduler load probe

Question: does the Scheduled Shadow scheduler hold up with many configured logic
configurations at a 15 s cadence?

Note: `docs/implementation/poc-master-plan.md` does not exist on this branch; the
15 s default minimum cadence comes from gap 19 in `poc-gap-register.md`.

## Method

`tests/WellSiteAutoPilot.SchedulerLoadProbe` drives the real
`ScheduledShadowSchedulerService` and `ExecutionService` with `DispatchProxy` fakes for
the three repositories and a virtual clock. Each fake query advances the clock by a
modelled round trip (default 1 ms, `PROBE_RTT_MS`); a dispatch write transaction is
modelled as 7 round trips (begin, advisory lock, exists, overlap, connection lock,
insert/scopes/audit, commit). Nothing touches Postgres, NATS, SignalR or the running
AppHost. Run: `dotnet run -c Release --project tests/WellSiteAutoPilot.SchedulerLoadProbe`.

Not measured (modelled or read from code only): real Postgres cost, NATS publish,
worker throughput, SignalR fan-out. Poll 5 s, cadence 15 s, random schedule offsets,
10 min virtual run.

## Measurements (RTT 1 ms)

| N | batch | queries in a sweep | CPU ms/sweep | max sweep s | lag p50/p95 s | configs ever dispatched | dispatched/expected |
|---|---|---|---|---|---|---|---|
| 10 | 100 | 39 | 0.5 | 0.10 | 2.9/5.0 | 10 | 407/400 |
| 100 | 100 | 459 | 4.5 | 1.0 | 3.1/5.2 | 100 | 4075/4000 |
| 1000 | 100 (default) | 459 | 3.6 | 1.0 | 3.1/5.2 | **100** | 4075/**40000** |
| 1000 | 500 (max) | 2331 | 11.2 | 5.0 | 3.7/6.5 | **500** | 20338/**40000** |
| 1000 | 500, RTT 5 ms | 5003 | 14.2 | 25.0 | 19.8/33.8 | 500 | 12000/40000 |

(`>` 100% at N=10 is the first sweep dispatching an occurrence from just before the
run start. Lag max in the first rows is that startup occurrence, up to one cadence.)

- Decision cost is negligible (about 11 ms CPU for 1000 configs). Cost is round trips.
- Steady lag is uniform in `[0, poll)` plus sweep time, about 2.5 s mean at poll 5 s.
- At 15 s cadence and 5 s poll every config is evaluated three times per occurrence.
- Overlap (N=10, run 40 s > 15 s): 140 of 400 occurrences dispatched; occurrences that
  arrive while a run is active are skipped, not queued, so effective cadence is 45-60 s.
  948 `OverlapBlocked` warnings were logged in 10 min (one per config per 5 s sweep).
- Pause/missed ticks (N=100, 10 min down): on resume exactly one dispatch per config
  (latest occurrence only); the 40 missed occurrences per config are dropped, no
  backfill, no burst.
- Sweep slower than poll (N=1000, RTT 5 ms): 24 overrun sweeps; `PeriodicTimer`
  coalesces ticks, so the sweep runs back to back and lag grows past one cadence, which
  means occurrences are silently skipped (the service only ever dispatches the latest).

## Bottlenecks found in code

1. **Silent starvation above the batch size (critical).** `ListActiveScheduledAsync`
   does `ORDER BY Name ... Take(limit)`. Default `Scheduler:BatchSize` is 100, max 500.
   Configurations beyond the batch are never evaluated, at any cadence. Probe: N=1000
   serves 100 (default) or 500 (max). Every sweep sees the same first-N by name.
2. **Single-threaded sequential sweep.** One `foreach` with awaited round trips per
   config; sweep time is linear in N times RTT (about 10 round trips per dispatch).
   One sweep must finish within the cadence or occurrences are lost.
3. **N+1 queries.** Per config per sweep: module lookup (`GetAsync`) and
   `ScheduledOccurrenceExistsAsync`, then `HasActiveAssetOverlapAsync`, then the write
   transaction repeats the occurrence-exists and overlap checks plus an advisory lock,
   a connection `FOR SHARE` lock, and a save. Already-dispatched configs still cost 2
   queries on each of the other two sweeps in the cadence. Roughly 14 queries per
   config per 15 s, about 0.9 q/s per configuration (about 930 q/s at N=1000), most of
   them redundant.
4. **Locks.** `pg_advisory_xact_lock` per configuration (no cross-config contention)
   and `FOR SHARE` on the shared Connection row: all configs viewing one Connection take
   a share lock on the same row; harmless alone but conflicts with a Connection edit.
   Dispatch is serial, so lock contention within one sweep does not arise.
5. **Overlap policy is skip.** `HasActiveAssetOverlapAsync` (same configuration and
   asset, active status) blocks the occurrence with a Warning each sweep. Skipped
   occurrences are not recorded. Effective cadence silently degrades to a multiple of
   the run time and log volume is 3 warnings per skipped occurrence per config.
6. **Same hosted loop runs the control gate sweep** (`AutonomousControlService.SweepAsync`
   with the same batch) after dispatch, so gate work delays the next dispatch.
7. **Fan-out.** One `Executions` change notification per sweep (not per config), so
   SignalR fan-out is O(clients), not O(N). Result consumers notify per result; at
   N=1000 and 15 s that is about 67 notifications/s. Not measured; coalesce/debounce
   server-side if the notifier does not already.
8. **NATS/worker.** Dispatch inserts a Requested execution (no inline NATS call in the scheduler code read) rather than
   publishing inline, so NATS publish is outside the sweep. Worker throughput must be at
   least N / cadence = 67 runs/s at N=1000, 15 s. Not measured here.

## Recommended limits and changes

Limits now: at most 100 enabled schedules per deployment at defaults; up to 500 only
with `Scheduler:BatchSize=500` and DB RTT under 1 ms; minimum cadence 15 s (as decided);
refuse cadence below 15 s at save and at dispatch.

Changes, in priority order (not done here, larger than a safe small fix):

1. Replace `Take(limit)` with due-based selection: store `NextDueUtc` (or compute
   in SQL) and select only configurations with a due, undispatched occurrence, ordered
   by due time; page through all candidates each sweep instead of one capped batch.
2. Batch the checks: one query for modules, one for occurrences already dispatched
   (`WHERE (revision, scheduledFor) IN ...`), one for active overlaps across all
   candidates; only the write stays per config. Cuts about 14 to about 2 queries per
   config per interval.
3. Process candidates with bounded parallelism (for example 8), each with its own
   scope/DbContext; keep the per-configuration advisory lock.
4. Skip evaluation of configs already dispatched for the current occurrence using a
   per-process cache keyed by `(revisionId, scheduledFor)`.
5. Add per-config jitter (hash of config id, up to 20 percent of cadence, applied to the
   first occurrence only) to spread load, and lower the poll to 1-2 s if lag below
   about 2.5 s matters.
6. Overlap policy: record a skipped occurrence (`Skipped/OverlapBlocked`) so it is
   visible in the Schedules UI; log once per occurrence at Information; consider
   `skip` as default with an optional `queue-one` policy.
7. Emit metrics: sweep duration, candidates, dispatched, lag histogram, batch
   saturation; alert when sweep duration exceeds half the poll interval.
8. Run the control-gate sweep on its own timer.

## Code change made

`ScheduledShadowSchedulerHostedService` now logs a Warning (event 2313) when the sweep
candidate count equals the batch size, because that is the silent starvation case.
Build-verified; no behaviour change otherwise.
