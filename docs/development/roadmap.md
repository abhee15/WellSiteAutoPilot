# Roadmap after the first Shadow slice

Status: proposed on 2026-10-18, for owner approval. Replaces the ad hoc ordering used so far.

This was written after comparing the requirements, architecture baseline and ADRs with the code. It
records what is missing, the decisions that several pieces share, and an order that avoids rework.

## 1. What exists and what does not

Built and tested: grant-based RBAC with asset scope, assets and asset types, module catalog (register,
approve, retire), Configured Logic with revisions, validation and activation, scheduled and manual Shadow
runs, JetStream outbox and consumers, recommendations and decisions, the Provider Simulator and one
simulator data provider in the Gateway, the web UI over REST with 10-second polling, installer and
bootstrapper skeletons.

Not built, although the architecture or an agreed follow-up names it:

| # | Gap | Evidence |
| --- | --- | --- |
| 1 | Real modules cannot run | The .NET worker resolves one built-in sample (`BuiltInLogicModuleResolver`). Registration takes a manifest and a checksum only; no package is uploaded, stored or loaded |
| 2 | Python worker | `main.py` waits for a stop signal and consumes nothing |
| 3 | Real providers | No CygNet or WAMI adapter; the Gateway has the simulator provider only |
| 4 | Control path | Message types exist. No policy, readiness, ownership, ControlAction persistence or Gateway command execution. Shadow only |
| 5 | SignalR | Named in the architecture baseline; no hub, package or client. The UX plan called it "server-sent events" (B10) |
| 6 | OpenTelemetry | Architecture says "from the first slice"; no reference anywhere |
| 7 | Maintenance and control inhibit | No code, although upgrades are meant to enter it |
| 8 | Readiness, licensing, system health | Basic health endpoints only |
| 9 | Audit and activity read API | Events are written, nothing reads them (B8) |
| 10 | Retention and backup | Neither built nor planned |
| 11 | Windows Negotiate in a real browser (#92) | Open release gate; blocked on field data |
| 12 | Generated enums | Agreed in `decision-followups.md`; grant fields are still strings |
| 13 | Module trust beyond approval | No signature or publisher verification |

## 2. Decisions several pieces share

Settling these first is what prevents rework. Each becomes an ADR when approved.

**D1. Module packages are stored in PostgreSQL and distributed to workers through the JetStream Object
Store, keyed by SHA-256.**
- The Server unpacks an uploaded `.wsamodule`, validates the manifest with the existing validator, stores
  the immutable package (table in the `logic` schema, `bytea`) and registers it Untrusted.
- Workers must not read business tables and do not call the Server over HTTP. The Object Store reuses
  the NATS connection and credentials they already have and works offline. The Server publishes a
  package to bucket `WSA_MODULES` when it is approved. A worker pulls by SHA-256, verifies the hash,
  caches it on local disk and loads it.
- Keying by SHA-256 means signatures and trusted publishers (gap 13) add metadata later without moving
  anything.
- To verify before building: NATS.Net 3.3.0 Object Store behaviour on the bundled server version.

**D2. Live updates are a coarse "changed" signal, never data.**
- The Server raises an in-process change notification after a transaction commits (one seam, used by
  SignalR now and by the activity feed later). The hub sends only an area name (`executions`,
  `recommendations`, `configured-logic`, `assets`, `modules`) to connections whose user holds that area's
  read permission. The browser then refetches through the existing authorized REST endpoints.
- This keeps scope filtering in exactly one place, so a push can never reveal an asset a person may not
  see. Polling stays as the fallback and the client shows connection state and freshness.
- Open item: how often to re-check a long-lived connection's permissions. Proposal: on every
  reconnect and at most every 60 seconds, close the connection when the user no longer holds the
  area's read permission.

**D3. SignalR authentication follows #92.** Negotiate on Kestrel is connection bound, and WebSockets
cannot add an `Authorization` header, so the hub cannot be designed until the real-browser behaviour is
known. Prepare the seam (D2) first; wire the hub after #92.

**D4. One readiness model.** Maintenance mode, control inhibit, the health page and the Overview all
need the same answer to "is the system ready, and for what". Define it once (gap 8) before the control
path (gap 4) depends on it.

**D5. Telemetry setup lives in one shared platform project** with W3C trace context carried in NATS
message headers. `CorrelationId` (business) stays separate from `TraceId` (technical), as the
architecture already says. Export is opt-in OTLP; nothing requires the Internet.

## 3. Order, with what each step needs

Dependencies decide the order. Items in the same row are independent and can run in parallel.

| Step | Work | Needs | Why here |
| --- | --- | --- | --- |
| 0 | This roadmap; correct B10 to SignalR | none | Stops the plan drifting |
| 1a | **OpenTelemetry** (gap 6) | none | Self-contained; everything built after it is traced from the start |
| 1b | **Generated enums** (gap 12) | none | Fewer new string contracts to migrate later |
| 1c | #92 Negotiate data | owner | Gates D3 |
| 1d | ADRs for D1, D2, D4 | owner approval | Prevents rework below |
| 2 | **Module packages, server side**: upload endpoint, package table and migration, register Untrusted, publish to Object Store on approval | 1d | Foundation for every real module |
| 3 | **Worker loader**: pull, verify, cache; load .NET modules in an isolated load context; keep the built-in resolver for tests | 2 | First real module runs |
| 4 | **End-to-end real package in CI**: build with `wsa module pack`, upload, approve, configure, run | 3 | Proves steps 2 and 3 together |
| 5 | **Change notification seam** (D2), then SignalR hub and client with polling fallback and connection state | 4 for events worth sending, 1c for the hub | Live updates without a second filtering path |
| 6 | **Python worker**: SDK contract over a subprocess, same loader and cache | 3 | Reuses the loader, so it comes after it |
| 7 | **Readiness model** (D4), audit and activity read API, maintenance mode | 1d | Shared by control and by the UI |
| 8 | **Provider adapters** (CygNet, WAMI) | owner detail on access; see `docs/architecture/cygnet-wami-integration-research.md` | Adapters sit behind contracts that already have tests. CygNet target is 9.7+ via the Bridge API |
| 9 | **Control path** | 3, 7, 8, design review | Safety critical: needs a written design and owner sign-off before any code |
| 10 | Package signing and trusted publishers; retention and backup; licensing status | 2 | Additive once packages are keyed by hash |

Rules that keep this logical:
- A step is one or more small PRs, each complete and usable on its own, as before.
- No step may add a second path that filters by scope; the browser refetches through REST.
- Control (step 9) is not started on implied approval.
- Design polish (palette, dark and high-contrast themes) and the web-only backlog continue in the gaps
  and are not on the critical path.

## 4. Parallel tracks

- **Telemetry (1a)** touches only start-up wiring in each process and the NATS publish and consume
  helpers. It does not change behaviour, so it can be built while the module work is planned.
- **Enums (1b)** is contract-only and can run beside either.
- **Steps 2 to 4** are one track with one owner at a time; splitting them would create the integration
  problems the order avoids.
- **Readiness (7)** can start once D4 is approved, in parallel with steps 3 to 6, because it does not
  touch module loading.

## 5. What I need from the owner

1. Approve this order, or reorder it.
2. #92 field data (`/api/v1/security/me` status, `Authorization` header prefix, `edge://policy`).
3. CygNet and WAMI access detail before step 8.
4. A review slot for the control design before step 9.
