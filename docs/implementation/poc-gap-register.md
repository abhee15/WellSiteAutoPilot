# PoC gap register and proposed actions

Status: working list from the local end-to-end PoC (branch `feature/poc-integration`).
Priority: P0 blocks trustworthy local testing, P1 needed for a credible pilot, P2 design/hardening before production.
Owner branch is where the work is happening.

| # | Pri | Area | Gap | Proposed action | Branch |
|---|---|---|---|---|---|
| 1 | P0 | UI | Runs/recommendations do not update in real time although SignalR exists. Only result consumers, scheduler dispatch and the connection monitor notify; intermediate states, control actions and kill switch do not. A connection admitted to no areas silently falls back to slow polling | Trace live under Windows auth; add notifications for the full execution lifecycle, recommendations, control actions, kill switch; new control ChangeArea; views refetch on it; connection-state indicator; tests | `feature/poc-integration` (e2e agent) |
| 2 | P0 | CygNet | RPOC_00003 fails `CYGNET_VALUE_NOT_NUMERIC` | Probe the point live; handle point/status types or return a clear reason | `feature/poc-cygnet-adapter` |
| 3 | P0 | CygNet | Demo data is from 2020, so modules need a ~10 year maximum age | Report point timestamp age in test/discovery; optional simulated-live backend with current timestamps; document the pattern | `feature/poc-cygnet-adapter` |
| 4 | P1 | Scheduling | Scheduled reads act as one configured user (`Scheduler:ReadAuthorityUserId`) | Service-principal / delegated authority: who a schedule acts as, grant re-check, revocation, audit | `feature/poc-recommendation-modes` |
| 5 | P1 | Modes | An approved Recommendation never becomes a control action | Approve -> policy gate -> simulator sink, idempotent, with expiry and audit; real sink still refuses | `feature/poc-recommendation-modes` |
| 6 | P1 | Modes | Autonomy policy editing is API-only; control uses `security.manage` | Policy editing UI and a dedicated `control.manage` permission | `feature/poc-recommendation-modes` |
| 7 | P1 | Modes | No readback verification or ambiguous-dispatch resolution; simulator sink is in-process | Readback/confirm in the sink; simulator command endpoint so Autonomous exercises HTTP | `feature/poc-recommendation-modes` |
| 8 | P1 | Tests | `ExecutionDispatchTests` not run (needs NATS) | Run with local NATS and fix | `feature/poc-recommendation-modes` |
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

Open questions for the owner are in `docs/design/wami-provider-and-multi-provider-logic.md`.
