# Scheduler architecture, UI and performance plan

Status: **proposed, for review. No production code accompanies this document.** It records probes run on 2026-11-13
against `develop` at `b216c22` (shallow clone) and the direction they support. Decisions that need an owner
answer are collected in section 9.

## 1. Summary

The scheduler backend exists on `develop`: `ScheduledShadowSchedulerHostedService` (registered in `Server/Program.cs`),
`ScheduledShadowSchedulerService`, `ScheduleOccurrences`, `EffectiveSchedule`, pause/resume/frequency control with a
history table, an adapter minimum-cadence floor, calendar recurrence, and a service-principal read authority. It
dispatches Shadow, Recommendation and Autonomous revisions by writing a `Requested` Execution plus an outbox message.

So "the dispatcher does not push scheduled tasks" is not a missing backend. The code shows three real problems:

1. **Silent failure.** Every non-dispatch decision (blocked, skipped, refused) is only a log line. Nothing is persisted
   and the UI cannot say why a schedule did not run. The most likely reason on a dev machine is that
   `Scheduler:ReadAuthority:ServicePrincipalUserId` is not set anywhere (AppHost, example config, installer), so every
   occurrence for a connection-backed Asset fails closed with `EXECUTION_READ_AUTHORITY_REQUIRED`. This is inferred from
   code and was not reproduced, because the Server could not be built here (section 3).
2. **A real performance defect** in the overlap check, measured below (8.5 s per 1,000-configuration sweep, fixed by one
   partial index to 7 ms).
3. **Missing policy and operability features**: no recorded skips, no catch-up policy, no jitter, no schedule-lag
   metric, no run-now, and a run history that grows about 10 GB per day at 1,000 schedules of 15 s.

Recommendation: keep the in-process, Postgres-backed scheduler and extend it. Do not adopt Quartz, Hangfire or Temporal
(section 4.1). Borrow their policy vocabulary.

## 2. What is verified in the code

| Area | Finding |
| --- | --- |
| Loop | `PeriodicTimer` every `Scheduler:PollIntervalSeconds` (5 s, clamped 1 to 60). One sweep per tick, then the control-gate sweep in the same loop. |
| Selection | `ListActiveScheduledAsync` pages every active scheduled configuration by name (the old silent `Take(limit)` cap is fixed). Each sweep loads every configuration with revisions and bindings, then computes due time in memory. Not due-based. |
| Occurrence | `ScheduleOccurrences.LatestDue` returns only the latest due slot. Missed slots are dropped, never queued. |
| Idempotency | Unique index `(ConfigurationRevisionId, ScheduledForUtc)` on `operations.executions`, an advisory lock per configuration, and pre-checks. This is sound. |
| Overlap | `HasActiveAssetOverlapAsync` joins `execution_asset_scopes` to `executions` and filters status after the join. Policy is skip only. |
| Visibility | `LogDecision` writes one Warning per blocked decision per sweep (3 per blocked configuration per 15 s at defaults). No table, metric or UI shows it. |
| Authority | Read authority is resolved per occurrence and audited. Unset means refusal for live reads (documented in `authorized-connection-reads.md`). |
| Metrics | No scheduler metric exists (no meter, no lag figure). |
| Pause/resume | Built (`ConfiguredLogicScheduleControlService`, state and change tables, UI panel). The PoC master plan item F4 is stale. |

## 3. Probes run and their limits

Environment: 2 vCPU, 8 GB, PostgreSQL 16.15 on loopback. The Server could not be built: `api.nuget.org` is denied by
this session's egress policy, so no C# was compiled and the repo's scheduler tests were not run. All numbers below are
SQL-level probes against a schema reproduced from the EF configuration (same table and column names, same indexes,
`RequestPayloadJson` assumed at 1.5 KB and the revision manifest at 3 KB). They show database cost, not application
overhead (EF, serialization, the 7 round trips per dispatch that the earlier load probe modelled).

Scripts are in `/home/claude/probes` (`seed.sql`, `q.sql`, `q2.sql`, `q3.sql`, `dispatch.pgb`); proposal 7 commits them
under `tools/`.

Seed: 1,000 configurations, 2,000 scheduled executions each (2,000,000 rows).

| Probe | Result | Reading |
| --- | --- | --- |
| Candidate listing, one page of 100 | 1.7 ms | Cheap. |
| Candidate listing, whole pass (1,000) | 1.6 ms | Cheap at this N; load grows linearly and every sweep repeats it. |
| `ScheduledOccurrenceExists` | 0.5 ms (index-only) | Fine. |
| Overlap check, one configuration, cold | 224 ms | Reads all 2,000 scope rows, then 2,000 primary-key lookups. |
| Overlap check, 1,000 configurations, warm | **8,535 to 11,190 ms** | Cost is linear in run history per configuration. A sweep of due configurations would exceed the 5 s poll. |
| Same check with partial index on active statuses | **7 to 9 ms** | About 1,000 times faster. |
| Due-claim (`next_due_utc` partial index, `FOR UPDATE SKIP LOCKED`), 50,000 schedules, 5 s window | about 17,000 due rows claimed and advanced in 0.3 to 0.8 s | Cost follows the due set, not the total. Time grew across sweeps because no vacuum ran; plan for `fillfactor` and autovacuum tuning. |
| Occurrence-ledger insert, 3,000 rows | 28 ms; 232 bytes per row | A per-occurrence ledger is cheap to write but not free to keep (section 4.4). |
| Dispatch write transaction (lock, 2 inserts), 1 / 4 / 8 clients | 969 / 2,418 / 2,402 per second | Database is not the limit at 67 dispatches per second (1,000 schedules at 15 s). Parallelism helps up to about 4 on 2 vCPU. |
| Storage | 2,000,000 executions = 3.4 GB; scopes 237 MB | About 1.7 KB per run. At 1,000 schedules of 15 s that is 5.76 million runs per day, about 10 GB per day. |

Earlier probe branch `probe/poc-scheduler-load` (virtual-clock harness against the real service) already showed:
decision cost is negligible, cost is round trips, a sweep slower than the poll silently drops occurrences, and
skipped-overlap occurrences degrade effective cadence to a multiple of the run time. Its starvation finding is fixed on
`develop`.

Not measured: worker and gateway throughput (67 runs per second is the load at 1,000 schedules of 15 s), NATS publish
rate, SignalR fan-out, EF overhead, a real multi-instance run, and the user's actual failure.

## 4. Backend direction

### 4.1 Build on the existing scheduler, not a library

Quartz.NET, Hangfire and Temporal give clustering, misfire handling and schedule objects. They would duplicate what
this product already has and must not bypass: immutable revisions with live state beside them, `EffectiveSchedule` as the
one resolver, grant-authorised writes, Execution requests committed with the outbox in one transaction, audit and
correlation IDs, offline on-premises operation, and the MVP policy of no parallel runtime implementations. The policy
vocabulary is what to borrow: Temporal and Cadence expose overlap policy (skip, buffer-one, cancel, allow-all), a
catch-up window, jitter, pause-on-failure and backfill.

### 4.2 Fix first, independent of design (PR 0)

Add a partial index on `operations.executions ("LogicInstanceId") WHERE "Status" IN (active statuses)` and make
`HasActiveAssetOverlapAsync` filter on `executions.LogicInstanceId` and status before joining scopes. The measured
effect is 8,535 ms to 7 ms per 1,000 checks. This needs an EF migration through the migrator (never at startup), a
parity test for the overlap result, and a note that development databases are reset per the MVP policy. The active
status set is already duplicated in SQL-translatable form elsewhere; reuse it and keep the drift test.

### 4.3 Due-based selection

Add `logic.schedule_due` (one row per configured logic: `configured_logic_id`, `revision_id`, `next_due_utc`,
`lease_until`, `version`) with a partial index on `next_due_utc`. The sweep claims
`next_due_utc <= @now ORDER BY next_due_utc LIMIT n FOR UPDATE SKIP LOCKED`, dispatches, and advances `next_due_utc` in
the same transaction as the Execution insert. Rules:

- `ScheduleOccurrences` stays the only place that computes occurrences. The row is a cache of
  `EffectiveSchedule` plus `NextAfter`; SQL never recomputes cadence.
- Written on activation, pause, resume, frequency change and after dispatch, in the same transaction as each.
- A reconcile pass every minute recomputes rows from the resolver and corrects drift, and a test pins
  parity for interval, calendar, DST, paused and ended schedules.
- The unique occurrence index stays as the backstop, so a second instance cannot double-dispatch.
- Pass the clock to the claim query as a parameter; do not call `now()` inside a long transaction, where it is frozen (this invalidated one early probe run, which was redone).

This removes the per-sweep load of every revision and binding, makes cost follow the due set, and also makes
multi-instance safe without leader election.

### 4.4 Occurrence outcomes: record streaks, not every tick

Persisting every decision per occurrence would cost about 232 bytes per row, which at 1,000 schedules of 15 s is
about 1.3 GB per day for the ledger alone. Dispatched occurrences already exist as Executions. So persist only what is
missing, as run-length streaks:

`logic.schedule_blocks(configured_logic_id, revision_id, reason_code, first_scheduled_for_utc, last_scheduled_for_utc,
occurrences_skipped, opened_at_utc, closed_at_utc)`

A new row opens when the outcome changes from dispatched to blocked or to a different reason, updates in place while
the same reason repeats, and closes on the next dispatch. Writes happen on transitions and at most once per minute while
a streak continues. Notify the change area only on open and close, not on every sweep. Reason codes already exist
(`SCHEDULER_*`, `EXECUTION_*`). Warning logs drop to one per streak open.

### 4.5 Policies (schedule JSON, additive, defaults preserve today)

| Policy | Values | Default | Notes |
| --- | --- | --- | --- |
| Overlap | `skip`, `queueOne` | `skip` | `queueOne` holds the single latest missed slot until the active run ends. |
| Catch-up | `none`, `latest`, `window(seconds)` | Shadow and Recommendation: `latest`. Autonomous: `window` bounded by the cadence. | An Autonomous run must never act on a stale occurrence; this is a safety decision (section 9). |
| Jitter | seconds, first occurrence only | 0 | Spreads load when many schedules start together; bounded by the next occurrence. |
| Pause on failure | after N consecutive failed runs | off | Pauses the live schedule through the existing control service, with an audited reason. |

Cron expressions, exclusion windows (blackouts), run-now and backfill are valuable but not needed to answer "why does
nothing run" or to be fast. They are phase 4 and the owner decides whether they ship in V1 (section 9).

### 4.6 Concurrency, backpressure and isolation

- Bounded parallelism for claimed rows, default 4 (measured plateau), each task with its own scope and context, and the
  per-configuration advisory lock retained.
- Move the control-gate sweep to its own timer so gate work cannot delay dispatch.
- **Backpressure:** when `Requested` plus `Queued` Executions exceed a threshold, defer dispatch with reason
  `SCHEDULER_BACKLOG`, recorded as a streak. Worker throughput is unmeasured and is the likely real limit.
- Sweep deadline: a sweep running longer than half the poll interval raises a warning and metric. A sweep longer than the
  poll must never silently drop slots; the streak table records them.
- Keep per-configuration failure isolation (already present).

### 4.7 Observability

Emit through the existing platform telemetry project (to confirm the meter API there): sweep duration, candidates
claimed, dispatched, blocked by reason, dispatch lag histogram (`now - scheduled_for_utc`), backlog depth, reconcile
corrections. Add a Scheduler component to system readiness (ADR-009): degraded when the read authority is unset while
live-read schedules exist, when lag p95 exceeds the budget, or when the sweep overruns.

### 4.8 Setup that makes it work

Document and seed the service principal for dev: create the security user, set
`Scheduler:ReadAuthority:ServicePrincipalUserId` in `appsettings.Local.example.json`, and give it scoped read grants.
Surface its absence in the UI and readiness check rather than in logs.

### 4.9 Retention (decision needed)

About 1.7 KB per run is a storage problem before it is a speed problem. Options: prune completed scheduled Executions
older than N days while keeping aggregates, partition `executions` by month, and trim payload JSON on completion. This is
outside the scheduler but gates any large fleet. The budget below assumes a decision exists.

## 5. Performance budgets (acceptance gates)

| Metric | Budget | Basis |
| --- | --- | --- |
| Dispatch lag p95 | at most poll interval + 1 s | Uniform lag in [0, poll) plus sweep time. |
| Sweep duration p95 | below half the poll interval at 1,000 schedules | Prevents slot loss. |
| Overlap check | single-digit milliseconds, independent of history length | Measured with the partial index. |
| Queries per due configuration | at most 3 reads plus one write transaction | Down from about 14. |
| Idle cost | proportional to due rows, not total schedules | Due-claim probe. |
| Overrun | zero silently dropped slots; every drop is a streak row | Section 4.4. |
| UI list | first paint of 200 rows under 1 s, server paged | Section 6. |

Verification: extend `tests/WellSiteAutoPilot.SchedulerLoadProbe` (on `probe/poc-scheduler-load`) from fakes to a
Postgres-backed run at 100, 1,000 and 5,000 schedules, record results in `docs/design/`, and keep the SQL probes as a
regression check on the two hot queries.

## 6. UI direction

### 6.1 Current state (read from code)

- `SchedulesOverview` is read-only. It joins the configuration list with the loaded Executions list **in the browser**
  (`buildScheduleOverview` and `latestRunByConfiguration`). Last run is therefore missing for any configuration whose
  runs fall outside the loaded page, and the cost grows with both lists. There is no paging, filter or search.
- `ConfiguredLogicSchedulePanel` (pause, resume, frequency, reason, change history) is solid. It refreshes through
  `useLiveResource` on the `configured-logic` area.
- Nothing shows why a schedule is not running, how far behind it is, or what was skipped.

### 6.2 Proposed

Keep the existing master/detail layout and shared components (`DataTable`, `Status`, `ContextLink`, `PanelLoading`,
sentence case, localization keys, accessible names). No new UI framework.

**List (server read model).** `GET /schedules?state=&mode=&assetId=&search=&limit=&after=` returns one row per
schedule with health computed on the server, so the browser stops joining lists. Scope filtering runs before paging, as
the repo requires, with scoped and unscoped parity tests.

```
Logic        Frequency      Health             Next run     Last run
CF-12 pump   every 15 s     Running            12:00:15     12:00:00  Completed
CF-07 plunger every 1 min   Blocked  (reason)  12:01:00     11:42:00  Completed
CF-03 shadow  daily 06:00   Paused             —            Yesterday Completed
CF-19 auto    every 30 s    Behind  (lag 40 s) 12:00:30     12:00:00  Running
```

Health is one of: Running, Paused, Ended, Blocked (with reason text), Behind. A summary strip above the table counts
each health so a person sees a problem without scanning rows. Blocked and Behind sort first.

**Detail pane** for the selected schedule: the policy summary (overlap, catch-up, jitter), the blocked streaks with
plain-language reasons and "since", the last 50 occurrences (dispatched Executions plus streaks, one timeline), the
existing change history, and a link to the setup panel. Reasons map each code to a sentence and a next step, for example
`EXECUTION_READ_AUTHORITY_REQUIRED` becomes "Scheduled reads have no service account. An administrator must set one."

**Actions.** Pause, resume and frequency stay as they are. Add bulk pause and resume (maintenance windows) and run-now
only if the owner confirms. Policy fields go in the setup panel and `AssignmentScheduleEditor` under an "Advanced"
disclosure, with plain-language labels ("If the previous run is still going", "After downtime", "Spread start times").

**Live updates.** Add a `Schedules` change area, raised on streak open or close and on schedule changes, never on every
sweep. The list refetches on it; polling stays as the safety net. This touches the change-area contract (server enum,
web registration, tests).

**Performance and accessibility.** Server paging and filters, no client-side join, stable row keys, no per-row timers
(one clock tick shared, as today), keyboard-operable controls, and no motion. Tests follow the existing layout:
`src/web/tests/*.test.mjs` for models and `tests/interaction/*.test.tsx` for the panel.

## 7. Delivery plan

Branches follow `feature/*` or `fix/*` into `develop`, as draft PRs, each with the PR template's design-impact
section. Development databases may be reset per the MVP policy; each migration PR documents the reset.

| PR | Scope | Gate |
| --- | --- | --- |
| 0 `fix/scheduler-overlap-index` | Partial index and query rewrite, parity test | Overlap probe at 1,000 configurations under 20 ms total; existing scheduler tests green |
| 1 `feature/scheduler-visibility` | `schedule_blocks` streaks, reason mapping, service-principal setup docs and AppHost example, readiness component, metrics, log reduction, `Schedules` area | A refused occurrence appears in the API and UI with a reason; log volume per blocked configuration is one line |
| 2 `feature/schedules-read-model` | Server `GET /schedules` with health and paging, UI list and detail pane, localization, tests | List renders 1,000 schedules server-paged; no browser join |
| 3 `feature/scheduler-due-selection` | `schedule_due`, claim with `SKIP LOCKED`, reconcile pass, bounded parallelism, separate gate timer, backpressure | Section 5 budgets met in the Postgres-backed load run; multi-instance duplicate test |
| 4 `feature/scheduler-policies` | Overlap `queueOne`, catch-up, jitter, pause-on-failure, setup fields | Policy matrix tests including DST and Autonomous bounds |
| 5 (decision) | Cron and calendar expressions, exclusion windows, run-now, backfill, bulk actions | Owner approval |

### 7.1 MVP re-scope (2026-11-13)

Decision from the owner: functionality first, performance later, with seams so later work does not rewrite the MVP.

MVP (build now):
- **PR 1 `feature/scheduler-visibility`** without the metrics and the `Schedules` area beyond what the UI needs: persisted
  blocked streaks with reason codes, plain-language reasons, service-principal setup in the AppHost example and docs,
  a readiness line when the authority is unset, and one Warning per streak instead of per sweep.
- **PR 2 `feature/schedules-read-model`** reduced to health and reason on the existing overview (server computes health,
  last run and blocked reason per schedule), so the page stops guessing from a client-side join. Paging and filters stay
  deferred.
- **PR 4 `feature/scheduler-policies`** reduced to what a pilot needs: an optional `overlap` and `catchUp` block on the
  schedule JSON, validated and defaulted to today's behavior (skip, latest), with Autonomous bounded.

Deferred until after MVP, behind seams added now:
- PR 0 (overlap index) and PR 3 (due-based selection, parallelism, backpressure): keep candidate listing behind one
  interface (`IScheduleCandidateSource`) and the overlap check behind the existing repository method, so each can be
  swapped without touching dispatch or the UI. PR 0 stays a one-line index and may ride along if convenient.
- Cron and exclusions, run-now, backfill, bulk actions, metrics, retention.

Seams to add now (cheap, no behavior change): schedule policy fields are optional and versioned in the schedule JSON;
outcomes carry a stable reason code; the health calculation lives in one server class that the UI only displays.

PR 0 and PR 1 together answer the original problem and are small. PRs 2 to 4 are the performance and feature
foundation. Nothing here changes provider contracts, the module SDK, or what a Logic Module may do.

## 8. Risks

- `schedule_due` can drift from `EffectiveSchedule`: mitigated by writing it only through the resolver, the reconcile
  pass, and a parity test.
- Autonomous acting on a late occurrence: mitigated by a bounded catch-up window by default for that mode.
- Worker throughput unknown: the backpressure gate and the lag metric make it visible before it hurts.
- Retention is unresolved and dominates long-term cost (section 4.9).
- Probes used a reproduced schema and assumed payload sizes; PR 0 and PR 3 re-run them against the real EF schema in CI.
- Local environment could not build the Server, so all application-level behavior above is read from code, not observed.

## 9. Decisions needed from the owner

1. Target scale: how many schedules and what shortest cadence in the first pilot. At 100 schedules of 60 s most of this is
   comfortable; at 1,000 of 15 s, retention and worker capacity are the issue.
2. Retention for scheduled Executions and their payloads.
3. Autonomous catch-up default: bounded window (recommended) or the same as other modes.
4. Overlap default stays `skip`; is `queueOne` wanted in V1.
5. Are cron expressions, exclusion windows, run-now and backfill in V1 or later.
6. Streak table (recommended) versus a full per-occurrence ledger.
7. Confirm the service-principal model for dev and installer (create it in the bootstrap, or document it as a manual
   step).
