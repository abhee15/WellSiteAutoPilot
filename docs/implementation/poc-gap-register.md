# PoC gap register and proposed actions

Status: working list from the local end-to-end PoC (branch `feature/poc-integration`).
Priority: P0 blocks trustworthy local testing, P1 needed for a credible pilot, P2 design/hardening before production.
Owner branch is where the work is happening.

| # | Pri | Area | Gap | Proposed action | Branch |
|---|---|---|---|---|---|
| 1 | P0 | UI | Runs/recommendations do not update in real time although SignalR exists. Only result consumers, scheduler dispatch and the connection monitor notify; intermediate states, control actions and kill switch do not. A connection admitted to no areas silently falls back to slow polling | Trace live under Windows auth; add notifications for the full execution lifecycle, recommendations, control actions, kill switch; new control ChangeArea (done: `control`, raised by the kill switch, policy, execute/verify/resolve endpoints and the gate); views refetch on it; connection-state indicator; tests | `feature/poc-integration` (e2e agent) |
| 2 | P0 | CygNet | RPOC_00003 fails `CYGNET_VALUE_NOT_NUMERIC` | Probe the point live; handle point/status types or return a clear reason | `feature/poc-cygnet-adapter` |
| 3 | P0 | CygNet | Demo data is from 2020, so modules need a ~10 year maximum age | Report point timestamp age in test/discovery; optional simulated-live backend with current timestamps; document the pattern | `feature/poc-cygnet-adapter` |
| 4 | P1 | Scheduling | Scheduled reads act as one configured user (`Scheduler:ReadAuthorityUserId`) | Service-principal / delegated authority: who a schedule acts as, grant re-check, revocation, audit | `feature/poc-recommendation-modes` |
| 5 | P1 | Modes | Approved Recommendation -> policy gate -> simulator sink is built (opt-in per configuration, idempotent, expiry, staleness limit, audit); the Execute action on approved recommendations is built in the inbox (confirmation panel, `control.manage` plus decide scope, every gate refusal explained); the policy-editing UI, kill-switch control and the resolve-ambiguous action are not | Web: policy editor, kill switch and resolve UI; real sink still refuses | `feature/poc-recommendation-modes` |
| 6 | P1 | Modes | Autonomy policy editing is API-only. The dedicated `control.manage` permission has landed (policy and kill-switch writes no longer use `security.manage`) | Policy editing UI | `feature/poc-recommendation-modes` |
| 7 | P1 | Modes | Readback verification and ambiguous-dispatch resolution are built (sink readback, `Verified`/`Ambiguous`, verify and resolve endpoints). Remaining: the simulator sink is in-process, so Autonomous does not yet exercise an HTTP command path; no UI to resolve | Simulator command endpoint (E5); resolve UI | `feature/poc-recommendation-modes` |
| 8 | P1 | Tests | Resolved: `ExecutionDispatchTests` runs in CI against a real NATS JetStream broker (job "Execution outbox to JetStream integration") and is green on `develop`; the PoC branch made no changes to it | None; extend it when dispatch behaviour changes | closed |
| 9 | P1 | Authoring | Builds run in trusted local mode with no isolation | Windows AppContainer + Job object for the compile step (kill-on-close, memory, timeout, tree cancel); prove cancellation | `feature/poc-authoring-local` |
| 10 | P1 | Authoring | Test state is always NotRun; no intermediate values | Fixture test runner outside the Server process with an explicit instrumentation contract | `feature/poc-authoring-local` |
| 11 | P1 | Authoring | No live-data preview | Authorized minimal preview through existing read-plan authority, no operational execution state | `feature/poc-authoring-local` |
| 12 | P2 | Authoring | Drafts are file-backed; structured/collection calculations only as opaque code; card-calibration cannot run on the production host (V2) | DB-backed drafts; collection steps; V2 host support; provider choice in the UI | `feature/poc-authoring-local` |
| 13 | P1 | CygNet | Discovery paging, id collisions across site/service, point map validation, MotorAmps unmapped, client built per read | Verify paging in UI path; per-connection map validation and UDCs; cache clients per domain/service; timeouts | `feature/poc-cygnet-adapter` |
| 14 | P1 | WAMI | Client never run against a live server | Live probe against the locally running WAMI server (port 5656); answer units, rerun-safety and session-limit questions | `feature/poc-wami-provider` |
| 15 | P1 | WAMI | Multi-provider modules are design-only | Manifest V3 named provider roles: smallest first PR with V1/V2 compatibility; first WAMI-backed sample module | `feature/poc-wami-provider` |
| 16 | P2 | Platform | Aspire DCP pin (13.5.1) is a local workaround; worker/Gateway opt-in Windows build for CygNet | Decide a supported local-dev path; keep out of production builds | integration |
| 17 | P2 | Security | Gateway uses one Windows account/licence for all CygNet connections; WAMI needs allowed-group access | Decide per-connection identity or service account model | design |
| 18 | P2 | Data | ForeSite wells mostly lack pump fillage (`FORESITE_MEASUREMENT_MISSING`) | Choose wells/quantities with data; extend ForeSite read capabilities as needed | integration |
| 19 | P1 | UX | Schedules are set inside configured-logic setup only; users cannot see what is scheduled, when it next runs, or adjust frequency from where they work | Proposal: (a) a read-only "Schedules" overview (config, target asset/view, mode, frequency, last/next run, last result, enabled) reached from the Logic area, with pause/resume and edit-frequency actions; (b) a schedule summary and inline editor on each asset/view page (and the configured-logic panel) so editing stays in context; both use the same schedule API and SignalR change area. DECIDED: schedules belong to the configuration (per-target overrides deferred); minimum frequency defaults to 15 s and is overridable per provider/adapter (declared by the adapter, enforced server-side on save and at dispatch, shown in the UI). Permissions: reuse existing `configuredlogic.manage` to edit/pause schedules (schedule is part of the configuration revision, same policy as the setup endpoints) and `logic.read`/`executions.read` for the overview; no new permission now. Revisit only if schedule editing must be split from configuration editing, or when `control.manage` (gap 6) lands for Autonomous policy | `feature/poc-recommendation-modes` + web |
| 20 | P1 | Providers / Control | Modules cannot change anything at a provider. Adapters are read-only and the gate's real-provider sink always refuses, so Recommendation and Autonomous runs end in the simulator. ForeSite needs setpoint updates and CygNet needs commands that adjust controller registry values | Shared reviewed design first: a command seam separate from each read client, per-connection write authority, idempotency token, readback verification, no blind retry of an ambiguous send, a dry-run sink that never writes, explicit per-connection opt-in, and the gate's bounds, rate limit and kill switch. Then the two integrations (gaps 21 and 22). Master plan workstream L | design: `docs/design/autonomous-real-sink-readiness.md`; after gaps 5 to 7 |
| 21 | P1 | ForeSite | Setpoint writes from a module's control intent to ForeSite | Use ForeSite's existing setpoint operations (prior art for bounds and step limits); confirm the interface, caller identity and licensing; treat any status other than a verified readback as unknown | blocked on gap 20 design and ForeSite access |
| 22 | P1 | CygNet | Controller commands (registry values) from a control intent | Issue UIS commands and confirm by data-group transaction; probe whether a direct point-write API exists before using it; Gateway account needs UIS command rights (gap 17) | blocked on gap 20 design |
Open questions for the owner are in `docs/design/wami-provider-and-multi-provider-logic.md`.

## Recommended sequence (feature by feature)

1. **Trust the platform (integration, cygnet-adapter): gaps 1, 2, 3.** Real-time SignalR updates; clear CygNet failure reasons (done, pending merge); freshness diagnostics and a stale-data demo profile. Nothing else can be judged until the UI reflects runs and data problems are explained.
2. **Make the modes real (recommendation-modes): gaps 4, 5, 6, 7, 8, then 19.** Scheduler identity, approved recommendation to the policy gate, policy UI with `control.manage`, readback and simulator endpoint, NATS dispatch tests. Then the schedule overview and in-context editor (configuration-scoped, 15 s default, per-provider minimum, `configured-logic.manage`), which builds on the scheduler identity.
3. **Make authoring usable (authoring-local): gaps 9, 10, 11, then 12.** Job-object isolation (done, pushed), test runner with intermediate values, live preview, then DB drafts, collection steps and provider choice. Gate: before anyone but you uses it, AppContainer isolation must be proven.
4. **Widen providers (cygnet-adapter): gap 13 (reads merged, #361 to #363) and (integration) 18.** Point-map validation, paging, client caching; pick ForeSite wells with fillage data. Runs in parallel with step 3.
5. **WAMI (wami-provider): gaps 14, then 15.** Live probe (done against the real server), then manifest V3 provider roles as the smallest backwards-compatible PR, then the WAMI-backed sample module live with ForeSite data and model.
6. **Production hardening (design): gaps 16 and 17.** Decide the supported local-dev path and the per-connection identity model before any pilot outside a developer machine.

7. **Provider command paths (design first): gaps 20, 21, 22.** After the control gate, readback and operator-approved dispatch exist, design the shared command seam, then ForeSite setpoints and CygNet controller commands. Real writes stay disabled until that design is reviewed and a connection is explicitly opted in.

Merge order into `feature/poc-integration`: cygnet-adapter, recommendation-modes, authoring-local, wami-provider (smallest conflict surface first), rebuilding and re-running the live path after each.

## Branch map (where the PoC work lives)

Repo: https://github.com/abhee15/WellSiteAutoPilot. All branches are pushed to origin.

| Branch | Purpose | Key docs |
|---|---|---|
| `origin/probe/authoring-build-package`, `origin/docs/console-web-authoring-plan` | Original probes and authoring plan (inputs) | `docs/design/console-web-authoring-plan.md`, `modules/authoring/probes/` |
| `feature/authoring-build-package` | Probes re-run on Windows, AppContainer/Job isolation probe, CI step | `modules/authoring/probes/CONFIDENCE-REVIEW.md` |
| `feature/poc-cygnet-adapter` | CygNet adapter (live/simulated), discovery, sample modules, freshness diagnostics | `docs/implementation/cygnet-adapter.md` |
| `feature/poc-authoring-local` | Local authoring API/UI, Job-object build isolation | `docs/design/authoring-local-poc.md` |
| `feature/poc-recommendation-modes` | Shadow/Recommendation/Autonomous modes, policy gate, kill switch, readback | `docs/architecture/execution-modes.md` |
| `feature/poc-wami-provider` | WAMI provider design, WCF client, multi-provider coordinator, WAMI sample module | `docs/design/wami-provider-and-multi-provider-logic.md` |
| `feature/poc-integration` | Merge of all of the above, e2e fixes, runbook, this register | `docs/implementation/local-poc-runbook.md` |
| `origin/poc/cygnet-reader` | Earlier CygNet POC findings (reference) | `poc/CYGNET-POC-FINDINGS.md` |

## Landing the PoC on `develop`

`develop` head is `d78865d` (#358, UI sentence case, 2026-10-31). `feature/poc-integration` is 0 behind and about 42 ahead (149 files), so it merges cleanly today. Land it as reviewable PRs, in this order, each rebuilt and tested on its own:

| Step | PR | Why this order | Gate before merge |
|---|---|---|---|
| 1 | CygNet adapter (`feature/poc-cygnet-adapter`) | Adapter-declared read rules and clear failure codes are the base every other change reads through; off by default (`Providers:CygNet:Backend`) | Offline tests, build with `CYGNET_LIBRARY_PATH` unset; live check on CYG_DEMO |
| 2 | Execution modes + control (`feature/poc-recommendation-modes`) | Recommendation/Autonomous behaviour, gate, kill switch, `control.manage`; real sink still refuses | `ExecutionModeSafetyTests`, `ExecutionDispatchTests` with NATS, permission catalog guard (#355) |
| 3 | Real-time `control` change area + e2e fixes (integration) | Small, fixes the visible UI complaint | `ChangeSignalChecks`, web tests |
| 4 | Schedules UI (gap 19) | Needs step 2 scheduler identity | New work, not started |
| 5 | Local authoring (`feature/poc-authoring-local`) | Off by default (`Authoring:LocalBuild:Enabled`); do not merge before isolation is proven | Job isolation tests pass and AppContainer test result recorded |
| 6 | WAMI provider (`feature/poc-wami-provider`) | Self-contained project, opt-in client, no behaviour change until V3 manifest support lands | WAMI provider tests; owner answers on DLL redistribution and identity |
| 7 | Probes + docs (`feature/authoring-build-package`) | Evidence and CI only | CI green on Windows step |

Not for `develop`: the local DCP pin (gap 16) and `appsettings.Local.json`; keep them out of the CygNet/Aspire PRs.