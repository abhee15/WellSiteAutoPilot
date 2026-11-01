# Live-update change signal (ADR-008)

The Server pushes a coarse "something changed" signal so the browser can refetch sooner than its
polling interval. The signal never carries data. This note describes what is built; the decision and its
reasons are in `docs/adr/ADR-008-live-update-signal.md`.

> **Open release gate (issue #92): Windows Negotiate behaviour for browser WebSockets is UNVERIFIED.**
> Negotiate on Kestrel is connection bound and a browser cannot add an `Authorization` header to a
> WebSocket. The hub uses the host's single authentication scheme, exactly like the REST API, and nothing
> else. Whether a real browser can authenticate a `/hubs/changes` connection against a Windows domain has
> not been tested. CI proves the hub only with the `TestHeader` scheme and a non-browser client. Until
> `tools/ci/verify-windows-negotiate.ps1` data exists, treat the hub as unproven in production and rely on
> the polling fallback. Long polling stays enabled by default as a possible fallback transport.

## Pieces

| Piece | Where | Role |
| --- | --- | --- |
| `ChangeArea`, `IChangeNotifier`, `NoOpChangeNotifier` | `WellSiteAutoPilot.Application/Changes` | The seam. A closed enum of areas (`Executions`, `Recommendations`, `ConfiguredLogic`, `Assets`, `Modules`, `Connections`). `Notify(area)` carries no identifiers or values and must not throw |
| `ChangeSignalService` | `Server/Changes` | The Server's notifier. Producers only set a flag; a background loop coalesces a burst per area (default 250 ms) and sends one message per area |
| `ChangesHub` | `Server/Changes`, route `/hubs/changes` | `[Authorize]` hub with no client-callable methods. On connect it admits the connection only to the area groups whose read permission the user holds, and closes it when there are none |
| `ChangeAreaAccess` | `Server/Changes` | Resolves the authenticated principal to a user and asks the grant store (via `GrantPermissionChecker`, shared with the REST policy handler) which area read permissions are held now. Claims are never trusted |
| `ChangeConnectionMonitor` | `Server/Changes` | Every `ChangeSignal:RecheckSeconds` (default 60) re-runs that check for each live connection and closes it if any admitted area is no longer held, or if the check fails. The client reconnects and is admitted to what it may now read |

## Area to permission

| Area (wire name) | Read permission |
| --- | --- |
| `executions` | `executions.read` |
| `recommendations` | `recommendations.read` |
| `configured-logic` | `configured-logic.read` |
| `assets` | `assets.read` |
| `modules` | `logic.read` |
| `connections` | `connections.read` |
| `control` | `recommendations.read` (kill switch, autonomy policies and control actions are read with it) |

The hub sends nothing derived from data, so no asset-scope filtering exists here. A holder of
`assets.read` over one Asset learns only that "assets changed" and then refetches through REST, where
scope filtering still runs before pagination.

## Where it is raised (after the commit)

- REST mutations: `.NotifiesChange(ChangeArea.X)` on the endpoint. The filter runs after the handler
  returns, that is after the service call committed. A handler that throws raises nothing.
  - `Assets`: create asset type, create asset
  - `ConfiguredLogic`: create, create revision, validate, activate
  - `Executions`: request a configured shadow execution
  - `Modules`: register, approve, retire
  - `Recommendations`: decide, execute an approved recommendation, verify or resolve a control action
  - `Control`: set the kill switch, set an autonomy policy, execute an approved recommendation, verify or resolve a control action
- `ExecutionResultConsumer` and `ExecutionResultV2Consumer`: `Executions` when a result is applied;
  the V2 consumer also raises `Recommendations` when materialization created any, and `Recommendations` and
  `Control` when the autonomy gate processed any intent.
- `ScheduledShadowSchedulerHostedService`: `Executions` when a sweep dispatched at least one request;
  `Recommendations` and `Control` when its gate sweep processed any intent.

Any new endpoint that changes one of these areas needs `.NotifiesChange(...)`.

## Guarantees and limits

- Best effort. A lost or failed notification is never a correctness problem; polling and the next refetch
  recover. Raising a notification cannot fail the request that raised it.
- Permission changes reach live connections within `RecheckSeconds` (default 60 s), not instantly.
- In-process only. A second Server instance would need a backplane for the seam.
- Not in the reviewed v1 API route group; `tools/ci/check_api_authorization.py` is unaffected.

## Settings (`ChangeSignal` section)

| Key | Default | Meaning |
| --- | --- | --- |
| `RecheckSeconds` | 60 | Permission re-check period for live connections (clamped 1 to 3600) |
| `CoalesceMilliseconds` | 250 | Burst window per area (clamped 0 to 5000) |
| `Transports` | `WebSockets, LongPolling` | Allowed hub transports, for example `LongPolling` only |

## Tests

`tests/WellSiteAutoPilot.IdentityBoundaryTests/ChangeSignalChecks.cs` covers the area contract,
permission mapping and revocation, coalescing, failure isolation and the monitor.
`tests/WellSiteAutoPilot.AssetApiTests/ChangeSignalWireChecks.cs` drives a real host over a WebSocket:
unauthenticated negotiate is refused, an administrator receives exactly the `assets` area after a commit
(no ids or values), and a user with no read permission is disconnected.
