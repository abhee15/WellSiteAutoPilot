# Real-time events: producer coverage and design

Status: probe (`probe/poc-realtime-events`). The typed-event slice in section 6 is built behind `ChangeSignal:TypedEvents`; the rest is design.
Note: `poc-master-plan.md` ends at section H. The "section I" named in the brief does not exist on this base, so this document is the source for that workstream.

## 1. Where we are

SignalR is in place (ADR-008): `ChangesHub` at `/hubs/changes`, `ChangeSignalService` (coalesces, 250 ms), `IChangeNotifier.Notify(ChangeArea)`, `ChangeAreaAccess` (current grants decide which areas a connection may hear, re-checked every 60 s), and web `src/live/*`.
Limits that make it feel like polling:

- The message is only an area name. The browser cannot tell what changed, so `App.tsx` refetches the whole workspace snapshot (about seven requests) for any of four areas.
- No identity of the change, no ordering, no way to know a signal was missed (a dropped send is only recovered by the 60 s safety poll).
- No user-facing notification: a failed run or a recommendation waiting for a decision is visible only if you are looking at the right page.

## 2. Producer coverage matrix

Legend: signal = `IChangeNotifier` area raised after commit. "Typed" = also publishes a typed event in this probe.

### Server state changes

| State change | Where | Signal | Typed |
|---|---|---|---|
| Execution requested (manual shadow) | `POST /configured-logic/{id}/revisions/{rev}/shadow` | Executions | requested |
| Execution requested (scheduled) | `ScheduledShadowSchedulerHostedService` | Executions | requested |
| Execution started / completed / failed (v2) | `ExecutionResultV2Consumer` | Executions | started, completed, failed |
| Execution completed / failed (v1) | `ExecutionResultConsumer` | Executions | completed, failed |
| Recommendation created | `ExecutionResultV2Consumer` after materialization | Recommendations | created |
| Recommendation decided (operator) | `POST /recommendations/{id}/decision` | Recommendations, Control | decided |
| Recommendation settled by control gate | consumer after `ProcessExecutionAsync`; scheduler `SweepAsync` | Recommendations, Control | decided (consumer only) |
| Kill switch, autonomy policy | `PUT /control/...` | Control (+ Recommendations, ConfiguredLogic) | no |
| Connection create/update/enable/test/delete | `/connections` routes | Connections | no |
| Connection health | `ConnectionMonitorHostedService` | Connections (only when something changed) | no |
| Asset types, assets, import | `/asset-types`, `/assets`, `/connections/{id}/import` | Assets | no |
| Configured logic create, revision, validate, activate | `/configured-logic` routes | ConfiguredLogic | no |
| Module upload, approve, retire, catalog from draft | `/logic-modules` routes, `/authoring/drafts/{id}/catalog` | Modules | no |

### Gaps (state changes with no signal, or with a signal that is too weak)

1. **Worker progress**: there is no progress message from the worker at all. The execution stays `Running` with no intermediate data. Needs a new NATS subject and contract (section 9).
2. **Execution states that no producer writes yet**: `Cancelled`, `Suspended`, `TimedOut` exist in the enum, but no server component sweeps stale runs, so there is nothing to signal. When a timeout sweeper is added it must publish.
3. **Scheduler non-dispatch decisions** (overlap blocked, module disabled or unavailable, invalid configuration, dispatch failed): logged only (events 2310 to 2312). An operator sees a schedule silently not run. Should become a notification (warning) and a visible "last decision" on the configuration.
4. **Recommendation expiry, superseded, invalidated**: time- or event-driven transitions with no signal. A stale item stays in the inbox until the next poll.
5. **Control gate outcomes on the v1 result path**: the v1 consumer neither materializes nor signals Recommendations/Control; fine while v1 is Shadow only, but a gap if v1 ever carries other modes.
6. **Authoring drafts** (create, edit, validate, build, build cancel): no area exists, and a build is long-running and finishes in the background, so the UI must poll it. Add an `authoring` area scoped to the owner and a `build` completed/failed event.
7. **Module package publication**: `ModulePackagePublicationDispatcher` completes asynchronously and signals nothing; the Modules list lags until polled.
8. **Connection import** signals Assets only; the connection's last-import metadata (Connections) is not signalled.
9. **Connection source checks** (`/source-checks`) and **discovery** do not signal. Source checks are the strongest candidate: they produce "changes since last import" that the operator should be told about.
10. **Saved asset views** (create, update, delete): no signal, so a second browser session of the same user does not update.
11. **Security administration** (roles, user grants, group grants): no area. The affected user's connection is closed at the next 60 s re-check, but nothing tells their open UI "your access changed"; the session is re-read only by polling. Needs a targeted per-user event (section 4).
12. **Outbox dispatch and NATS redelivery or dead-letter** (a request that never reaches a worker): no state change is surfaced. This is where "the run is stuck in Requested" should become a notification.
13. **Delivery to the browser** is best effort and unnumbered: a failed send, a restart or a dropped connection loses signals silently. Mitigated today only by poll and refetch on reconnect. Typed events add a sequence so loss is detectable.
14. **Producers call `Notify` after commit but not transactionally** (no outbox). A crash between commit and notify loses the signal. Acceptable while polling is the net; for notifications that must not be lost (failed run) section 8 persists them.

## 3. Event envelope

Sent as hub method `event` next to the existing `changed`.

```json
{
  "v": 1,
  "epoch": "9c1f…",          // one server process run
  "area": "executions",       // stream the sequence belongs to
  "sequence": 42,             // per area, per epoch, starts at 1
  "entity": "execution",
  "id": "4444…",
  "kind": "failed",           // requested | started | progress | completed | failed | created | decided
  "occurredAt": "2026-10-31T23:10:04Z",
  "actor": "CORP\\operator",  // null for system producers; "scheduler" for the scheduler
  "correlationId": "…",       // same value as the execution's CorrelationId and NATS envelope
  "payload": { "status": "Failed", "code": "MODULE_ERROR" }
}
```

Rules:

- **Minimal payload**: short state labels only (status, result or failure code, percent for progress). Never measurements, outputs or names. Anything richer is read from the authorized REST endpoint. A client that does not hold the row asks `GET /executions/{id}`, which applies asset scope.
- **Sequence is per area stream**, not global, because a user hears only the areas they may read; a global counter would show false gaps.
- `epoch` changes on restart; a client seeing a new epoch discards its last-seen numbers and reads the area.
- Delivery is at most once, in order. A gap (`sequence != last + 1`) means "read the area again" (the existing area refetch), so correctness never depends on the event stream.

## 4. Subscriptions and authorization

- **Today and in the probe**: events go to the existing group `area:{name}`, joined only for areas where `ChangeAreaAccess` finds the read permission. Reuse is exact, including the 60 s re-check that closes connections.
- **Known limit**: executions are asset-scoped in REST but the area group is not. An event therefore tells a user with `executions.read` but a narrower asset scope that an execution id exists and its status. The id is an unguessable GUID, the payload has no asset, name or value, and the follow-up read is scope-filtered, so no data leaks, but existence and timing do. Acceptable for the probe; not for production.
- **Per-entity groups (next)**: add client methods `Subscribe(entity, id)` and `Unsubscribe`, group `entity:execution:{id}`. The hub validates with the same REST authorization (`GetAuthorizedAsync` through `IExecutionRepository`), so the run detail view gets progress for exactly the run it shows. Re-validate on the 60 s recheck.
- **Scoped fan-out (needed before production)**: group `asset:{assetId}` per asset a user may read, and publish events to the groups of the entity's assets. `ChangeAreaAccess` grows from "areas" to "areas plus asset scope", computed by the same grant evaluator the REST layer uses.
- **Per-user events** (access changed, own decision confirmed): group `user:{userId}`, joined automatically.

## 5. Resync after reconnect, heartbeat, indicator

- **Resync**: today, every (re)connect triggers a full refetch. With sequences: the client remembers `(epoch, last sequence)` per area. On reconnect it calls a hub method `GetStreamHeads()` (authorized areas only) and compares. Same epoch and equal heads: nothing missed, no refetch. Otherwise refetch only the areas that moved. No server-side replay buffer is needed for correctness; an optional ring buffer of the last N events per area would allow `Replay(area, afterSequence)` for small gaps.
- **Heartbeat**: SignalR already pings (default keep-alive 15 s, client timeout 30 s). Keep them. Add an application heartbeat only if proxies strip pings: an `epoch`-carrying `hb` every 30 s that also lets the client notice a silent stall.
- **Indicator**: `LiveStatus` exists (connecting, connected, reconnecting, polling). Add "last event received" to its tooltip and a state "stale" (connected but no heartbeat for 2 intervals).

## 6. Prototype slice (built)

Server (`ChangeSignal:TypedEvents=true` enables it; default off, area signals unchanged either way):

- `IChangeEventPublisher` / `ChangeEventService`: per-area sequences, bounded queue (1024, drops newest so overload shows as a gap), single ordered sender, never throws.
- Producers publish next to their existing `Notify`: manual and scheduled run requested; run started, completed, failed (v1, v2); recommendation created and decided.
- `progress` is part of the contract and the client, but nothing produces it yet.

Web:

- `ChangeSignalClient` handles `event`: tracks `(epoch, sequence)` per area; a gap or epoch change falls back to the area refetch.
- `App` applies run events to the Runs list in place (status patch; one authorized `GET /executions/{id}` for a run it does not hold) and no longer refetches the snapshot when a typed event already covered the area signal that follows it (within 2 s).
- Notification centre (bell, list, toast): run failed, recommendation awaiting decision (only for users who may decide), dismiss and deep link.

## 7. Multi-instance backplane options

Today everything is in one process: area bits, sequences and group membership are local.

| Option | How | Pros | Cons |
|---|---|---|---|
| SignalR Redis backplane | `AddStackExchangeRedis` | Smallest change; groups work across nodes | New Redis dependency; sequences must still be unique per stream (see below) |
| Azure SignalR Service | Managed | No sticky sessions | Cloud dependency, not for a site-local deployment |
| NATS fan-out (recommended) | Producers publish event drafts to `wsa.events.{area}`; every server instance subscribes and sends to its local clients | NATS is already required (JetStream); no new infrastructure; also lets the worker publish progress straight to the bus | Sequence assignment must move to the publisher side or to JetStream |

With NATS: use a JetStream stream `WSA_EVENTS` (subjects `wsa.events.>`, limits retention, short max age, memory or file). The **stream sequence is the event sequence** (one stream per area, or filter by subject with per-subject sequence tracking), which gives a free, authoritative, multi-instance sequence and a replay buffer for resync. Each web node uses an ephemeral ordered consumer per area and forwards to its hub groups. `epoch` becomes the stream id. This replaces `ChangeEventService` internals, not the wire format.

## 8. Worker progress and notification centre

**Progress streaming**: the worker publishes `wsa.execution.progress.v2` (`executionId`, `percent` 0 to 100 or null, `phase` short label, `at`) at most once per second per run, core NATS (not JetStream: progress is disposable). The server consumer validates, does not persist, and publishes a `progress` event (`payload.percent`, `payload.phase`) on the executions stream. The run detail shows a progress bar; the list shows nothing extra. A terminal state always overrides progress; clients drop a progress event older than the terminal state.

**Notification centre**

- **Severity**: `critical` (failed run in Autonomous mode, kill switch engaged, connection lost), `warning` (run failed in Shadow, schedule blocked, recommendation about to expire), `info` (run completed, recommendation decided by someone else), `success` is not a notification.
- **Source of truth**: for anything that must not be missed, persist a `notifications` row (id, user or role, severity, entity, id, createdAt, ackedAt) when the server raises it, and push a `notification` event. Clients load unread on start, so a notification survives a closed browser. The prototype is client-only and per-session.
- **Preferences**: per user per category and severity: toast, centre only, off. Defaults: critical and warning toast, info centre only. Stored server-side; applied in the client for display and in the server when deciding what to persist.
- **Ack**: ack is per user and removes it from the unread count; "needs a decision" items also clear themselves when the recommendation is decided by anyone (the `decided` event).
- **Deep links**: every item carries a hash route (`#operations/{executionId}`, `#recommendations/{id}`) that the existing router already resolves.
- **Audit**: acknowledging is not audited (it changes nothing). Raising a critical notification is already covered by the event that caused it.

## 9. Recommended order

1. Turn the flag on in the demo environment and watch the sequence gaps in the console.
2. Close producer gaps 3, 4, 6 and 12 (silent failure modes) before more UI.
3. Per-entity subscription for the run detail plus the `progress` contract in the worker.
4. Server-side notification table with ack and preferences.
5. Replace the in-process publisher with a JetStream stream when a second instance is planned; the wire format stays.
6. Scope fan-out by asset before enabling for users with narrow scopes.
