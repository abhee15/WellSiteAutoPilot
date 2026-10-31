# Local PoC runbook (live CygNet + ForeSite)

Reproduces, on a developer machine with Windows authentication, the full chain: connect, discover and import,
author a module, build and approve it, run it in Shadow, Recommendation and Autonomous modes, and schedule it.
Real provider writes stay disabled; the only control sink is the simulator.

Observed on branch `feature/poc-integration`. IDs below are from one run and will differ on yours.

## 1. Start the stack

Set these in the shell that starts the AppHost (not in the shell you build or test from):

| Variable | Value | Purpose |
|---|---|---|
| `CYGNET_LIBRARY_PATH` | `<ForeSiteService>\Trunk\Libraries\CygNet` | Opt-in Windows build of the Gateway CygNet backend |
| `Providers__CygNet__Backend` | `live` | Use the real CygNet client |
| `Authoring__LocalBuild__Enabled` | `true` | Local authoring build (local dev only) |
| `Authoring__LocalBuild__StorePath` | `C:\wsa-dev\authoring` | Build store |
| `Control__Autonomous__Enabled` | `true` | Global autonomous flag (default off) |
| `Scheduler__ReadAuthorityUserId` | persisted user id of the bootstrap admin (`security.users`) | Authority for scheduled live reads (see gaps) |

```powershell
Start-Process pwsh -ArgumentList '-NoProfile','-Command','dotnet run --project dev/WellSiteAutoPilot.AppHost --launch-profile Windows *> C:\wsa-dev\apphost.log' -WindowStyle Hidden
```

Server is `http://$env:COMPUTERNAME:5080`. Call it with Windows auth:
`curl.exe -s --ssl-no-revoke --negotiate -u : http://$env:COMPUTERNAME:5080/api/v1/<route>`.
Builds and tests run in a separate shell with `CYGNET_LIBRARY_PATH` unset. The test projects need a freshly migrated,
empty database (`WSA_DATABASE_CONNECTION_STRING`, then `dotnet run --project tools\WellSiteAutoPilot.DatabaseMigrator`).
Stop the AppHost by process id before rebuilding: its child processes lock the build output.

## 2. CygNet connection, discovery, import

1. `POST /connections` with adapter `cygnet`, domain `27212`, site service `CYG_DEMO.UIS`, facility filter `RPOC_00*`.
2. `POST /connections/{id}/test`: Healthy, acting as the Windows user.
3. `POST /connections/{id}/discover`: 107 facilities and a `snapshotId`.
4. `POST /connections/import/preview`, then `/import`, with `levels:["category"]` and objects
   `{kind:"cygnet.facility", externalId:"RPOC_00001"}` (also 00002 to 00004): 4 wells and 2 groups created.
5. ForeSite: `POST /connections/{id}/discover` on the existing healthy connection returned in about 2 s, complete, with
   several hundred wells already imported as assets.

## 3. Author, build, approve, load

1. `POST /authoring/drafts` with the `threshold-alert` starter (or an explicit document), targeting `PumpFillage`.
   An action may carry `"command": "SetFillageTarget"` (plus `quantity`, `unit` and a `suggestedValue`); it then emits
   a governed control intent instead of an advisory recommendation.
2. `POST /authoring/drafts/{id}/validate`, `/build` (about 2 s), `/catalog`.
3. `POST /logic-modules/{moduleId}/{version}/approve {"reason":"..."}` (local dev only).
4. `GET /logic-modules/{moduleId}/{version}/package-state`: wait for `preflightReady: true` (the package outbox published
   it to the NATS object store; the Worker loads it by sha256, so no extra port of the e2e commit was needed).
5. Versions are immutable. The demo CygNet point timestamp is 2020-10-17, so `maximumAgeSeconds` was set to 315360000;
   a 1-year version failed with `EXECUTION_INPUT_STALE` as expected.
6. CygNet sample: publish `modules\samples\cygnet\<n>\*.csproj`, then
   `dotnet run --project tools\WellSiteAutoPilot.Cli -c Release -- module pack <manifest> <payload> <out>.wsamodule`, then
   `POST /logic-modules/packages` (`application/octet-stream`) and approve. `pump-fillage-low-alert` 1.0.0 was uploaded and
   approved (sha256 `4bc7203d...beeb4`). Its manifest has a 120 s maximum age, so it fails as stale on the demo data.

Authored modules used: `authored.fillage-threshold-alert` 1.0.0 and 1.0.1 (advisory), and `authored.fillage-control-demo`
1.0.0 (command action `SetFillageTarget`, parameter `low-threshold` = `"99.9"`, PumpFillage default point `RRLPFILAVG`).

## 4. Configured logic and modes

Create with `POST /configured-logic` (data binding `providerId:"cygnet"`, `providerAssetExternalId:"CYG_DEMO.UIS::RPOC_00001"`,
`requirementId:"pump-fillage"`, optional `mode` and `schedule`), then `/revisions/{r}/validate`,
`/activate` (`If-Match: none` for the first activation), `/shadow {requestId}`.

| Mode | Observed |
|---|---|
| Shadow, CygNet RPOC_00001 | Completed, `AUTHORED_MODULE_COMPLETED`, PumpFillage 99.67 % read live |
| Shadow, ForeSite `RPOC_00001_9033` (provider `foresite`, external id 1338) | Completed, 99.6 %. Most other ForeSite wells fail `FORESITE_MEASUREMENT_MISSING` (no data) |
| Recommendation, RPOC_00001 | `REVIEW_LOW_FILLAGE` recommendation `SetFillageTarget` 99.9 % created per run; approved one, rejected another via `/recommendations/{id}/decision`; no control action created |
| Autonomous, RPOC_00002 (scheduler only; manual runs are refused) | With policy `PUT /control/policies/{id}` (`enabled`, limits `SetFillageTarget` `%` 0 to 100, max 5 per 3600 s) and the global flag, a `Dispatched` / `GATE_ALLOWED` action through the simulator sink each minute |
| Kill switch | `PUT /control/kill-switch {engaged:true}`: next two scheduled actions `Blocked` / `GATE_KILL_SWITCH`; released: next action `Dispatched` again |

## 5. Scheduled execution

Create the configuration with `schedule: {enabled:true, cadenceSeconds:60, startAtUtc:<UTC>, timeZoneId:"UTC"}`; activate it.
Executions with `trigger:"Scheduled"` appeared once per cadence (`GET /executions?limit=500`). An RPOC_00003 Shadow schedule
ran and failed `CYGNET_VALUE_NOT_NUMERIC` (that well's point is not numeric), which shows failures are recorded per run.

## 6. Fixes made for the PoC

- Advisory `RecommendationIntentDraft`s are now materialized as recommendations in Recommendation mode only
  (command `ADVISORY_REVIEW`, no suggested value required). Autonomous materializes control intents only; Shadow nothing.
- Authoring actions can carry a `command`, generating a `ControlIntentDraft` (validated: identifier command, suggested value required).
- Scheduled dispatch could never pin a live read (`EXECUTION_READ_AUTHORITY_REQUIRED`, reported as a generic dispatch
  failure). `Scheduler:ReadAuthorityUserId` supplies an explicit read authority; its grants are still re-checked when the
  Gateway resolves each read plan. Without it the decision now reports the read-authority reason code.
- Tests: AuthoringTests, ExecutionModeSafetyTests, RecommendationPersistenceTests, ScheduledShadowSchedulerTests.

## 7. Known gaps

- The scheduled-read authority is a single local opt-in user, not a designed service account with its own grants and audit.
- An approved Recommendation does not yet become a control action; only the Autonomous gate dispatches, and only to the simulator.
  Real provider writes remain refused (`REAL_PROVIDER_WRITES_DISABLED`).
- Demo CygNet data is stale (2020), so modules need very large maximum ages; authored `command` modules declare no command
  requirements in the manifest.
- The CygNet sample modules ship with a 120 s maximum age and emit only advisory drafts.
- ExecutionDispatchTests needs a NATS server with matching credentials and was not run locally.
- Scheduled configurations and the Autonomous policy keep running until deactivated; the policy was disabled at the end.
