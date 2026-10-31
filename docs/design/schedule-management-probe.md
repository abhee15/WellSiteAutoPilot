# Schedule management probe (gap 19)

Branch `probe/poc-schedule-ux`, based on `feature/poc-integration`. This is a prototype: it proves the API, the
enforcement and the UI shape. It is not hardened for production.

## Decisions taken as given

- A schedule belongs to the configuration. Per-target overrides are deferred.
- Minimum frequency defaults to 15 s. An adapter may declare a higher minimum. It is enforced on save and at dispatch,
  and shown in the UI.
- Editing and pausing need `configured-logic.manage`. Viewing needs `configured-logic.read`. No new permission.

## Findings: how schedules work today

**Storage.** A schedule is `ConfiguredLogicSchedule(Enabled, CadenceSeconds, StartAtUtc, TimeZoneId)` inside each
immutable `ConfiguredLogicRevision`. Only the active revision is ever dispatched. There is no separate schedule table
and no per-schedule state beyond the executions it produced.

**Editing.** The only path was a new revision: create, validate, activate (`ConfiguredLogicSetupPanel`, which POSTs to
`/configured-logic/{id}/revisions`). The panel has a schedule checkbox, an enabled checkbox, cadence (`min=1`), start
and timezone. Changing the frequency therefore meant re-entering the whole revision. There was no cross-configuration
view and no pause.

**Validation.** `ConfiguredLogicService.ValidateSchedule` checked only cadence > 0, the timezone, and the
ScheduledOneShot profile. There was no floor.

**Dispatch.** `ScheduledShadowSchedulerHostedService` polls every 5 s and calls
`ScheduledShadowSchedulerService.DispatchDueAsync`. For each active scheduled revision it computes the latest due
occurrence on the cadence grid anchored at `StartAtUtc`, skips an occurrence that already exists
(`ScheduledOccurrenceExistsAsync`), and skips with `OverlapBlocked` when an execution of the same configuration and
assets is still Requested/Queued/Starting/Running/Waiting. Scheduled live reads run under `ScheduledReadAuthority`.
The overlap policy (skip if the previous run is still running) therefore already existed; it is now reported in the
API as `overlapPolicy: "skip-if-running"`.

## What the prototype adds

### Minimum frequency

- `AdapterDescriptor` gained an optional `MinimumScheduleSeconds`. No adapter declares one yet (CygNet, ForeSite and
  PetroVisor are all null), so every configuration gets the 15 s default.
- `SchedulePolicy.ResolveMinimum` takes the highest declared minimum across the providers a configuration reads
  (data-binding providers, resolving a Connection id to its adapter, plus the view's connection).
- Save: `ConfiguredLogicService` throws `CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM` (validation) when a new revision's
  cadence is below the minimum. Dispatch: the cadence is clamped up to the minimum, so schedules stored before the
  rule (or before an adapter raised its minimum) do not hammer a provider.

### API

| Route | Policy | Notes |
| --- | --- | --- |
| `GET /api/v1/schedules?limit&assetId&viewId` | ConfiguredLogicRead | One row per active revision with a schedule. |
| `PUT /api/v1/configured-logic/{id}/schedule` | ConfiguredLogicManage | Body `{ cadenceSeconds?, enabled? }`. |
| `POST /api/v1/configured-logic/{id}/schedule/pause` | ConfiguredLogicManage | |
| `POST /api/v1/configured-logic/{id}/schedule/resume` | ConfiguredLogicManage | |

Writes require `If-Match: "<activeRevisionId>"`; a stale token returns `CONFIGURED_LOGIC_REVISION_CONFLICT` (409).
Writes notify the `configured-logic` change area.

`ScheduleResponse`: `configuredLogicId`, `name`, `activeRevisionId`, `revisionNumber`, `mode`, `targets[{assetId,name}]`,
`viewId`, `enabled`, `cadenceSeconds` (stored), `effectiveCadenceSeconds` (after the floor), `minimumCadenceSeconds`,
`startAtUtc`, `timeZoneId`, `nextRunAtUtc`, `running`, `lastRun{executionId,scheduledForUtc,status,resultCode,failureCode}`,
`canManage`, `overlapPolicy`.

### Update design: revision clone

Because revisions are immutable, an update clones the active revision with only the schedule changed, then validates
and activates it. A no-op change returns the current state without a new revision. This keeps the audit trail and the
existing activation rules; the cost is revision churn (see open questions).

### Web

- `SchedulesPanel` (`src/web/src/views/SchedulesPanel.tsx`): table of configuration, target, mode, frequency (stored,
  effective and minimum), state, last run and result, next run. Edit, pause and resume show only when the user has
  `configured-logic.manage` and the row says `canManage`. It refetches on the `configured-logic` and `executions`
  change areas, with polling as the safety net, and explains a below-minimum or conflict refusal.
- Mounted in three places: a **Schedules** tab in the Logic area (`#logic/schedules`, needs `configured-logic.read`);
  inline under a selected configuration in the configured-logic workspace; and on asset detail and the saved-view
  browser (hidden when the asset or view has no schedule).
- The setup panel's own schedule fields are unchanged. Its cadence input still says `min=1`; only the server error
  surfaces the minimum there.

## Tests

- Server: `dotnet run --project tests\WellSiteAutoPilot.ScheduleManagementTests` (policy math, resolver, save
  enforcement, list/update/pause/resume, stale token, no-op, raised minimum, dispatch clamp). No database needed.
  Added to CI as "Verify schedule management".
- Web: `tests/interaction/schedules-panel.test.tsx` (read-only vs manage, per-row `canManage`, SignalR refetch,
  If-Match and below-minimum message, pause/resume routes). The existing permission-catalog and sentence-case guards
  pass; `tools/ci/check_api_authorization.py` lists the four routes.
- Not run: the Postgres-backed scheduler tests, and nothing was exercised against a running AppHost.

## Open questions

1. **Revision churn.** Every pause or resume writes a revision. A mutable schedule outside the revision, or a
   "schedule-only" revision kind, would avoid this but weakens immutability.
2. **Atomicity.** The update is create, validate, activate. A failure midway can leave a Draft revision behind.
3. **Fail-open floor.** If the gateway adapter catalog is unreachable, the floor falls back to 15 s.
4. **N+1 reads.** The overview does one execution query and one asset lookup per schedule. Needs a batched query
   before it is used at scale.
5. **Permissions.** `/schedules` is gated by ConfiguredLogicRead plus execution scope; `executions.read` is not
   checked separately.
6. **Coverage gaps.** Only active revisions with a schedule are listed. View names are not resolved (the API returns
   `viewId`). Per-target overrides are deferred.
7. **Silent clamp.** Dispatch clamps without an event; the UI shows stored against effective cadence, but there is no
   audit entry when the clamp bites.
8. **Adapter minimums.** No adapter declares one yet; real values need to come from each provider's limits.
9. **Generated types.** The generated API client is not committed; the web typecheck here used an OpenAPI file
   extended by hand. CI generates it from the running server.

## Recommended next step

Decide the update model (open question 1 and 2). Then batch the overview queries, record clamping in the audit
trail, set real adapter minimums, and try the flow against the running AppHost.
