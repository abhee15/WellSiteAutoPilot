# PoC master plan: workstreams, priorities, probes and branches

Companion to `poc-gap-register.md` (gaps and PR landing order). This file is the complete work breakdown so nothing is missed.
Priority: P0 blocks trustworthy testing, P1 needed for a credible pilot, P2 hardening/design, P3 later.
Status: Done / In progress / Not started / Probe (a branch exists to answer an unknown before committing to a design).

## Workstreams

### A. CygNet adapter (`feature/poc-cygnet-adapter`)
| # | Pri | Item | Status |
|---|---|---|---|
| A1 | P0 | Clear value-failure reasons (no value, non-numeric, busy) | Done, pushed |
| A2 | P0 | Freshness diagnostics and stale-data demo profile | Done, pushed |
| A3 | P1 | Point-map validation, discovery paging/truncation, id collisions, client caching and call gate | Done (code); live re-check pending |
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
| E1 | P1 | Scheduler service identity replacing `Scheduler:ReadAuthorityUserId` | Not started |
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