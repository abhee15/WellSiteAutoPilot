# PoC master plan: workstreams, priorities, probes and branches

Companion to `poc-gap-register.md` (gaps and PR landing order). This file is the complete work breakdown so nothing is missed.
Priority: P0 blocks trustworthy testing, P1 needed for a credible pilot, P2 hardening/design, P3 later.
Status: Done / In progress / Not started / Probe (a branch exists to answer an unknown before committing to a design).

## Landed on `develop` so far

`feature/poc-integration` is the reference; work reaches `develop` as small reviewed PRs, one feature at a time, each green on all CI checks before merge.

| PR | What | Items |
|---|---|---|
| #361, #362, #363 | CygNet adapter in three slices: core and read rules, per-connection point map/paging/cached clients, failure reasons/freshness/bounded calls | A1, A2, A3 |
| #364 | Scheduled live reads act as a designated service principal (re-checked and audited per occurrence) | E1, gap 4 |
| #365 | The revision's execution mode reaches every Execution; Shadow and Recommendation supported, Autonomous refused until the control gate exists | Prerequisite for E2 to E5 |
| #366 | Provider command paths recorded as workstream L and gaps 20 to 22 | L1 to L8 |
| #367 | Control foundation: autonomy policies, control actions, kill switch, the gate (inert until wired) | E2 to E4 |
| #368 | Autonomous becomes a governed mode: scheduled, materialized, gated; scoped `/control/*` API | E2 to E4, gap 5 groundwork |
| #369 | Dedicated `control.manage` permission for control writes | gap 6 (backend) |
| #370 | A person-approved recommendation executes through the gate, opt-in per configuration | gap 5 |
| #371 | Sink readback, `Verified`/`Ambiguous` outcomes, verify and operator-resolve endpoints | gap 7 |
| Advisory recommendations (this change) | Module advice is recorded in Recommendation mode under a reserved, never-executable command | PoC `5667a27` |

## Workstreams

### A. CygNet adapter (`feature/poc-cygnet-adapter`)
| # | Pri | Item | Status |
|---|---|---|---|
| A1 | P0 | Clear value-failure reasons (no value, non-numeric, busy) | Done, merged (#363) |
| A2 | P0 | Freshness diagnostics and stale-data demo profile | Done, merged (#363) |
| A3 | P1 | Point-map validation, discovery paging/truncation, id collisions, client caching and call gate | Done, merged (#362, #363); live re-check pending |
| A4 | P1 | Per-connection point map and UDC editor in UI | Not started |
| A5 | P1 | Site-specific MotorAmps mapping and extra quantities (pressures, rod load, runtime, status) | Not started |
| A6 | P2 | Write capability survey (CygNet control APIs) feeding autonomous readiness | Probe `probe/poc-autonomous-real-sink-readiness` |
| A7 | P2 | Per-connection identity instead of one Gateway account/licence (gap 17) | Design |
| A8 | P2 | Subscription/push instead of polling for current data | Not started |

### B. ForeSite adapter and data
| # | Pri | Item | Status |
|---|---|---|---|
| B1 | P1 | Choose wells with fillage data (gap 18); document which quantities are available | Not started |
| B2 | P1 | Live `ModelFiles/CurrentWellModel/{wellId}` route verified for WAMI | Not started (needs ForeSite access) |
| B3 | P2 | Extend ForeSite read capabilities as modules need them | Not started |

### C. WAMI provider (`feature/poc-wami-provider`)
| # | Pri | Item | Status |
|---|---|---|---|
| C1 | P1 | Live probe over WCF net.tcp 5656 (open session, load model, nodal analysis) | Done, pushed |
| C2 | P1 | Manifest V3 named provider roles: first PR (model + validation + role bindings), V1/V2 compatible | Prototyped in tests; install/run not wired |
| C3 | P1 | Run-time support for V3 in snapshot, compatibility checks and worker hosts | Not started |
| C4 | P1 | ForeSite data + ForeSite model file + WAMI calc, live end to end | Partly (scripted data, local .wfl) |
| C5 | P2 | Units conversion, rerun safety after ambiguous timeout, worker limit (`WorkerLimitFault`) handling, WCFAllowedGroups identity | Open owner questions |
| C6 | P2 | Interfaces DLL redistribution decision | Owner decision |
| C7 | P3 | WAMI as an external adapter exposing selected functions | Later |
| C8 | P2 | Which local server build runs: installed 2023 vs P4 net472 Debug (running) vs CoreWCF net10 | Note: P4 net472 Debug works on 5656 |

### D. Logic module authoring (`feature/poc-authoring-local`)
| # | Pri | Item | Status |
|---|---|---|---|
| D1 | P0/P1 | Build isolation: Job object (done), AppContainer (implemented, test result pending) | Partly |
| D2 | P1 | Test runner with fixtures and intermediate values | In progress |
| D3 | P1 | Live-data preview through the read-plan authority, no operational executions | In progress |
| D4 | P1 | Provider/adapter choice and requiredAdapterKey in the authoring UI | In progress |
| D5 | P2 | Collection/structured calculations and V2 host proof (card-calibration) | In progress |
| D6 | P2 | DB-backed drafts and draft build state on the hub | In progress |
| D7 | P2 | Authoring UX redesign (templates, validation, diff, version history) | Design: `probe/poc-ux-designs` |
| D8 | P2 | Package signing/trust and promotion workflow (untrusted to trusted) | Not started |
| D9 | P3 | Multi-provider roles in the authoring UI | After C2/C3 |

### E. Execution modes: Shadow, Recommendation, Autonomous (`feature/poc-recommendation-modes`)
| # | Pri | Item | Status |
|---|---|---|---|
| E1 | P1 | Scheduler service identity replacing `Scheduler:ReadAuthorityUserId` | Done, merged (#364) |
| E2 | P1 | Approved recommendation to policy gate to simulator, with idempotency and expiry | Done, pushed (`ad9100b`) |
| E3 | P1 | `control.manage` permission and policy editing UI | Permission done; UI in progress |
| E4 | P1 | Readback and ambiguous-dispatch resolution | Done (code), UI controls in progress |
| E5 | P1 | Simulator command HTTP endpoint and sink | Built, unverified |
| E6 | P1 | `ExecutionDispatchTests` with NATS | Not started |
| E7 | P2 | Real sink readiness: what a real write to CygNet/ForeSite needs (interlocks, setpoint bounds, change windows, operator confirmation, rollback) | Probe `probe/poc-autonomous-real-sink-readiness` |
| E8 | P2 | Recommendation lifecycle: expiry, supersede, de-duplication, notifications | Not started |
| E9 | P2 | Mode promotion rules (Shadow to Recommendation to Autonomous) with evidence thresholds | Design |
| E10 | P2 | Audit trail and export for every gate decision | Not started |

### F. Scheduling (gap 19)
| # | Pri | Item | Status |
|---|---|---|---|
| F1 | P1 | Schedules overview and in-context editor, per configuration | Design/probe: `probe/poc-schedule-ux` |
| F2 | P1 | Minimum frequency: 15 s default, per-provider override declared by adapter, enforced on save and dispatch | Decided; not started |
| F3 | P1 | Permission: reuse `configured-logic.manage` | Decided |
| F4 | P1 | Pause/resume, next/last run, overlap policy (skip/queue if previous run still running), missed-run catch-up | Not started |
| F5 | P2 | Scheduler load and fairness with many configurations at 15 s | Probe `probe/poc-scheduler-load` |
| F6 | P2 | Time zones, calendars/blackout windows, jitter | Not started |
| F7 | P3 | Per-target schedule overrides | Deferred by decision |

### G. Web UI and UX (`feature/poc-integration`, designs in `probe/poc-ux-designs`)
| # | Pri | Item | Status |
|---|---|---|---|
| G1 | P0 | Real-time updates: `control` area added; Autonomous panel refetches | Done, pushed (`8779324`) |
| G2 | P1 | Finer-grained refetch (list/detail instead of whole snapshot) and authoring build state on the hub | Not started |
| G3 | P1 | UX designs: schedules, autonomy policy, recommendations inbox, authoring, provider roles, connection/point-map editor | Design: `probe/poc-ux-designs` |
| G4 | P1 | Run detail: show provider reads, freshness, intermediate values, gate decision | Not started |
| G5 | P2 | Accessibility, localization (sentence case guard #358), role-based visibility audit (#355) for every new screen | Per screen |
| G6 | P2 | Empty/error states for provider failures with the new reason codes | Not started |

### H. Platform, security, quality
| # | Pri | Item | Status |
|---|---|---|---|
| H1 | P2 | Supported local-dev path: DCP pin 13.5.1, Windows auth profile; keep out of production (gap 16) | Documented |
| H2 | P2 | Identity model for providers and scheduled reads (gap 17) | Design |
| H3 | P1 | CI: build/test without CygNet libs; Windows isolation step; web tests | Partly |
| H4 | P2 | Observability: metrics/logs per provider call, schedule lag, gate decisions | Not started |
| H5 | P2 | Secrets handling for connection credentials; per-environment config | Not started |
| H6 | P2 | Data retention for executions, recommendations, control actions, drafts | Not started |
| H7 | P1 | Docs: runbook, per-workstream docs, ADRs for decisions above | Ongoing |
| H8 | P2 | Pilot readiness checklist (what must be true before running against a real site) | Not started |

### L. Provider command paths: ForeSite setpoints and CygNet controller commands (design first)
Why: Recommendation and Autonomous runs must end in a change at the provider, not in the simulator. A module may need to update **setpoints in ForeSite**, and for **CygNet** the platform must **issue commands that adjust controller registry values**. Today every adapter is read-only (CygNet's `ICygNetClient` has no write method), the gate's real-provider sink always refuses (`REAL_PROVIDER_WRITES_DISABLED`), and policies can only name the Simulator sink. This is a deliberate safety stance, so this workstream is design-led and each step is opt-in.

| # | Pri | Item | Status |
|---|---|---|---|
| L1 | P1 | Shared design review: a command seam per adapter, separate from the read client; per-connection write authority (service principal, provider permission and licence refusals reported as distinct, non-retryable failures); unique idempotency token per ControlAction; readback verification after every write; ambiguous-send handling with no blind retry; a dry-run sink that validates and records but never writes; explicit per-connection opt-in plus the gate's bounds, rate limit and kill switch; audit of every attempt | Not started. Inputs: `docs/design/autonomous-real-sink-readiness.md` (read-only source survey, nothing executed) |
| L2 | P1 | ForeSite setpoint writes: ForeSite already sends setpoints through its own setpoint operations and has prior art for bounds and step limits, so it is the better first integration point. Confirm the callable interface from the Gateway, caller identity, licensing, status semantics ("success" text is not proof; verify by readback) | Not started (needs ForeSite access and an owner decision on the interface) |
| L3 | P1 | CygNet commands: issue UIS commands to adjust controller registry values; confirm delivery by a data-group transaction; a direct point-write API was not inspected and needs a probe before it is considered | Not started (depends on L1; Gateway account needs UIS command rights, see gap 17) |
| L4 | P1 | Readback and ambiguous-dispatch resolution in the control path (E4) is a precondition: a "sent" status never marks a command applied | Depends on E4 |
| L5 | P1 | Operator-approved dispatch in Recommendation mode (E2) reuses the same command seam, so a person approving a recommendation can result in a real write once opted in | Depends on E2, L1 |
| L6 | P2 | Interlocks and change management: setpoint bounds per asset, change windows, operator confirmation, rollback | Design; see E7 |
| L7 | P2 | Pilot gate: run first against a non-production site with the dry-run sink, then supervised writes | Not started; see H8 |

## Probe branches (unknowns that need evidence first)

| Branch | Question | Output |
|---|---|---|
| `probe/poc-schedule-ux` | What schedule data exists, how is it enforced, what minimal API and UI edit works in context? | Working prototype of overview + inline editor with min-frequency enforcement; notes |
| `probe/poc-scheduler-load` | Does the scheduler hold up with many configurations at 15 s cadence (lag, overlap, DB, NATS)? | Load script, measurements, recommended limits |
| `probe/poc-ux-designs` | What should the screens look like for F1, D7, E3, C2/D9, A4? | Design docs/wireframes using the existing component system |
| `probe/poc-autonomous-real-sink-readiness` | What would a real write need (CygNet/ForeSite APIs, safety), without performing any write? | Capability survey, safety checklist, dry-run design; no writes |

Safety rule for all probes: no writes to real providers; read-only against CYG_DEMO and ForeSite.

## Suggested execution order

1. Finish and merge in flight: A (CygNet), E (modes), G1, D1 (isolation), C1 (WAMI) per `poc-gap-register.md` landing order.
2. Next wave: E1, E6, F2/F4 (needs F1 probe), G2, G3 designs, D2/D3/D4.
3. Then: A4/A5, B1/B2, C3/C4, E7 outcomes, F5 outcomes.
4. Hardening: H2, H4, H5, H6, H8, C5/C6.

### I. Event-driven, real-time behaviour (`probe/poc-realtime-events`)
Owner direction: the system should behave like one that listens for actions and shows updates and notifications as things happen. Today SignalR only sends area names; every signal makes the client refetch the whole workspace snapshot.
| # | Pri | Item | Status |
|---|---|---|---|
| I1 | P1 | Event model: typed events (entity, id, kind, version/sequence, actor, correlation id) instead of area-only signals; keep area signals as fallback | Probe |
| I2 | P1 | Delivery: targeted refetch or patch from event payload; per-entity/group subscriptions (asset, configuration, execution) with authorization | Probe |
| I3 | P1 | Reliability: sequence numbers, missed-event detection and resync after reconnect, heartbeat, connection state indicator | Probe |
| I4 | P1 | Notification centre and toasts: run failed, recommendation awaiting decision, gate blocked, kill switch, provider unhealthy, schedule lag; per-user preferences, severity, ack/dismiss, deep links | Design/probe |
| I5 | P1 | Run progress streaming from the Worker (queued, reading, calculating, gate, done) and live status on runs and detail | Probe |
| I6 | P2 | Producer coverage matrix: every state change in server, scheduler, worker, gateway and authoring raises an event; tests enforce it | Probe |
| I7 | P2 | Multi-instance: SignalR backplane or NATS fan-out so events reach clients on any server instance | Design |
| I8 | P2 | Provider health push (connection monitor) and data freshness changes | Not started |
| I9 | P3 | External notification channels (email, webhook, chat) fed from the same events | Later |

## Recommendations and ideas (not yet in any workstream)

1. **Make the scheduler due-based.** Fix the silent cap (only `Scheduler:BatchSize` configurations are ever dispatched), batch the per-configuration checks, add jitter, record skipped occurrences, expose schedule lag as a metric (see `docs/design/scheduler-load-probe.md`). P1, before any pilot.
2. **Shadow-to-autonomy promotion with evidence.** Track, per configuration, shadow-vs-actual agreement, recommendation acceptance rate and false-alert rate; only allow promotion when thresholds hold for a period. A "what would have happened" report for any past window. P2.
3. **Run replay and explainability.** Store the inputs (values, timestamps, quality, model hash) and intermediate values for each run so any result can be replayed or compared across module versions. P2.
4. **Module lifecycle.** Versioned releases, canary on a few wells in Shadow, compare against the previous version, one-click rollback. P2.
5. **Data-quality layer.** Common freshness, quality and unit handling across providers, so logic receives `value, unit, age, quality` instead of provider-specific shapes. P1 (needed for CygNet and WAMI alike).
6. **Provider health as a first-class state.** Provider health feeds scheduling (pause or degrade when a provider is unhealthy), notifications and run reasons. P1.
7. **Dry-run first for control.** Use the dry-run sink on real systems (read-only) before any real write; show would-write values next to current values. P1.
8. **Idempotent, resumable work.** Result consumers and dispatch must tolerate redelivery; add dead-letter handling and an operator view of stuck runs. P2.
9. **Cost and licence guard.** Per-provider rate limits and concurrency (CygNet call gate exists; add WAMI worker-limit and ForeSite limits) with visibility in the UI. P2.
10. **Operator experience.** Saved views, per-well timeline (reads, runs, recommendations, actions, operator decisions in one stream), mobile-friendly recommendation approval. P2.
11. **Environment parity.** One command to bring up the local stack (AppHost + NATS + simulator + optional WAMI), documented, with seeded demo data and a stale-data profile. P1.
12. **Contract tests per adapter.** A shared adapter conformance suite run against simulators and (opt-in) live read-only systems. P1.
13. **Security review before pilot.** Authoring sandboxing, package trust, permission catalog, audit export, secrets. P1 gate.

## Using `feature/poc-integration` as the reference branch

Yes, with these rules:
- It is the integration branch for the PoC: always based on current `develop` (0 behind at `d78865d`), and every workstream branch merges into it.
- Branch new work from it as `feature/<area>-<topic>` and open PRs back into it; promote to `develop` through the PR order in `poc-gap-register.md`.
- Some work is still in flight on workstream branches (see the branch map) and is not merged yet; check the "Status" columns before relying on an item.
- It carries local-only items that must not go to `develop`: the DCP pin (gap 16) and local run settings. The probe branches (`probe/poc-*`) are evidence/prototypes; take what is useful through PRs.
- Merge `develop` into it regularly so it never drifts.

### J. CygNet relative (linked) facilities and hierarchy (`probe/poc-cygnet-relative-facilities`)
Finding from ForeSite source (`Server/Support/CygNetWrapper/CygNetRelativeFacilityHelper.cs`, `CygNetWellManager.cs`, ForeSiteToolbox `RelativeFacilityMapping*`): CygNet exposes relative facilities through `CygNet.API.Facilities.Relative`. Definitions are loaded from a CygNet settings package (`RelativeFacilityContent`) per FAC service and resolved from an origin facility with a named relation (`ResolveRelativeFacility(origin, relation, facClient)`). ForeSite layers a `RelativeFacilityMapping` per well type: each well quantity, additional UDC and command maps to a relation, so a point can live on a related facility (controller, meter, device) instead of the well facility. Tags are re-pointed through `GetRelativeFacilityPointTag`. Our CygNet adapter today only reads points on the discovered facility itself.
| # | Pri | Item | Status |
|---|---|---|---|
| J1 | P0 | Adapter reads must resolve quantity point tags through relation mappings (otherwise wells whose data sits on related facilities show missing data) | Probe |
| J2 | P1 | Determine whether the hierarchy (field > route > well > devices) can be built from relations or facility attributes, and what the relation names/depth are on CYG_DEMO | Probe |
| J3 | P1 | Import: asset tree from relations, with related device facilities as child assets or as data sources of the well | Design |
| J4 | P1 | Mapping UI: per well type quantity/UDC to relation, reusing ForeSite mapping definitions where available | Design (see UX doc connections-point-map.md) |
| J5 | P2 | Commands: relation-aware command targets for future control sink | After J1 |

### K. Adapter experience: connect, understand, author (`probe/poc-adapter-experience`)
Owner direction: make it easy to connect a software system through an adapter, understand what the adapter exposes, and author logic modules against it. Proposal: an Adapters tab that shows each adapter's interface.
| # | Pri | Item | Status |
|---|---|---|---|
| K1 | P1 | Adapter self-description schema (capabilities, quantities with units/types, discovery, read/write, limits, minimum cadence, auth, health, examples) | Research + design (advanced model) |
| K2 | P1 | Adapters tab: catalog, interface per adapter, health, connections using it, example modules | Probe prototype |
| K3 | P1 | Guided connect flow: settings form from schema, test connection, browse and preview data | Design |
| K4 | P1 | Authoring uses the schema: typed input picker, autocomplete, validation against capabilities, live preview (D3) | Design |
| K5 | P2 | Adapter authoring SDK for new adapters: contract, scaffold, conformance tests (recommendation 12) | Design |
| K6 | P2 | Versioning and compatibility between adapters and modules | Design |

## Status at integration (all branches merged, not rebuilt or re-tested together)

`feature/poc-integration` now contains every workstream and probe branch. The merged result has NOT been built or tested as a whole; individual branches were verified as noted in their docs. Check these first when you build:
- Manual conflict resolutions in `Server/Program.cs`, `ScheduledShadowSchedulerService` (authority provider + schedule floor), `AutonomousControlService` gate (DryRun sink + approved recommendations), `WellSiteAutoPilot.slnx`.
- EF migrations from authoring (`AddAuthoringDrafts`, not applied) and modes (`AddAutonomousControl`) share the model snapshot; regenerate/verify the snapshot.
- Web generated API types are absent locally; run `npm run generate-api` then typecheck.

Findings from the last probes and the items they add:
| # | Pri | Item |
|---|---|---|
| L1 | P1 | Scheduler silently serves only `Scheduler:BatchSize` configurations (100 default, 500 max); due-based selection with paging, batched checks, jitter, recorded overlap skips (`docs/design/scheduler-load-probe.md`) |
| L2 | P1 | Schedule edit creates a new revision per change; decide revision-clone vs schedule outside revisions; batch the overview queries; audit cadence raised by the floor; real adapter minimums (`docs/design/schedule-management-probe.md`) |
| L3 | P1 | Relative facilities: CYG_DEMO has no relation definitions and RPOC wells have no parents; facility attribute 22 (Parent Facility) is one level deep on 197 facilities; relationMap prototype in the adapter not built (`docs/design/cygnet-relative-facilities.md`); needs a site with real definitions |
| L4 | P1 | Authoring: AppContainer fails at process creation here (Win32 203), Job-object mode is the baseline; database draft store and migration unverified; live preview untested against live providers |
| L5 | P1 | Real-time: no worker progress producer, groups not asset-scoped, notifications client-only, no backplane; not tested against the AppHost |
| L6 | P1 | Adapters: WAMI not registered as an adapter; no adapter declares a minimum schedule; adapter profile endpoint and tab have no tests (`docs/design/adapter-experience.md`) |
| L7 | P1 | Modes: web policy editor, `ExecutionDispatchTests` with NATS, and simulator HTTP sink tests are unfinished or unverified |
| L8 | P2 | Real sink: ForeSite `SendSetPointsParamsForSingleWell` is the recommended write path; CygNet point-write API unconfirmed; dry-run outcome shows as Dispatched (`docs/design/autonomous-real-sink-readiness.md`) |