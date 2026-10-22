# View assignment continuation checkpoint

The execution workspace disconnected before the assignment implementation could be published. This branch currently contains this checkpoint only. Do not treat the assignment feature as delivered or merge this checkpoint as implementation.

## Completed and merged

- #253: revised design and reuse plan.
- #254: Connection-scoped hierarchy children and search.
- #255: explicit single-Connection well Views, hierarchy checkboxes, branch selection across paging, selected-well review, Connection-based visibility and create/edit/delete permissions.
- #255 exact head `2e266f000e581d713b38cb96ba2fd2cd575f3777` passed all seven CI jobs, including PostgreSQL 10,000-member checks, authorization HTTP checks, Shadow execution and Windows installer smoke testing.
- #255 merged into develop as `48c7a3cf36a685165a4feb4ae43c8d0a34c0151b`.
- Chromium preview at 1440 and 390 pixels confirmed the editor works without horizontal overflow.

## Workspace to recover

Local repository: `/workspace/scratch/5b5e1c094f24/WellSiteAutoPilot`.
Local branch: `feat/view-logic-assignment-snapshots`.
The assignment changes were uncommitted at disconnection. Preserve the working tree before switching branches or rebasing. Generated API client files under `src/web/src/api/generated/v1/` are local build products and must not be committed. The remote branch was created after disconnection and has only this checkpoint; reconcile that commit with recovered local work when publishing.

The last attempted exec could not create a process. A read-only recovery attempt returned `409 Conflict, environment_offline: Environment is not connected.` No further filesystem verification was possible.

## Assignment implementation prepared locally, not delivered

- Optional View reference on create/revision commands and HTTP contracts: View ID, expected revision and role.
- Server resolves the complete authorized member set; request-supplied targets must match it. All roles stay within the same Connection.
- Immutable revision provenance: View ID/revision, Connection ID and role alongside the explicit Asset binding snapshot.
- Optional module `requiredAdapterKey`, validated as an identifier, requires a compatible enabled Connection and a View assignment.
- A transaction guard locks the View, verifies the expected revision and exact membership again, and checks the Connection before saving. Referenced View deletion is refused with a structured conflict.
- View data inputs must use the owning Connection's adapter and each Asset's persisted source identity, preventing a binding to another provider or well.
- Validation/activation/manual dispatch retain an enabled Connection; scheduled listings skip disabled Connections and dispatch checks hold the Connection through enqueue.
- Asset validation uses a bulk ID lookup; explicit AllAssets grants avoid per-well grant lookups while retaining the existing post-lock authority checks. Exact-scope behavior is preserved.
- Logic setup offers authorized Views, loads full paged membership, filters adapter/cardinality incompatibility, records the reviewed revision, and verifies the persisted receipt before enabling validation. Existing individual-Asset setup remains supported.
- A single-well module still accepts a one-well View. Automatic fan-out into independently observable per-well executions is not implemented; do not increase module cardinality or describe that path as complete.
- Live ForeSite engineering reads/writes remain separate adapter/provider work. Current Gateway engineering-data routing registers only the simulator; these View changes do not establish real ForeSite execution.

## Maintenance issue found and prepared fix

The merged editor conservatively blocks editing an unavailable membership. Once assignment references forbid deletion, inactive wells could strand a View. The local follow-up distinguishes complete inspection authority from execution eligibility: broad authorized Asset readers can see inactive member names solely to remove them; restricted readers still cannot obtain hidden IDs or replace unseen membership. Assignment rejects unavailable members. Added server/UI regression cases for this repair path. This fix is local and not included in #255.

## Tests and verification to resume

Observed successful local checks during assignment work:

- Full .NET solution build: zero warnings/errors before the last maintenance/source-binding edits.
- Generated-client TypeScript/Vite production build and frontend lint.
- 198 node tests and a full 186-test interaction run, including complete View assignment, stale selection, incompatible adapter/cardinality, response revision verification, and inactive-member repair.
- Fifteen Python convention tests and the reviewed API inventory.

Added real-PostgreSQL checks in `tests/WellSiteAutoPilot.AssetPersistenceTests/AssetViewChecks.cs`: 10,000-well assignment, immutable scope after View edits, stale revision refusal, referenced deletion, wrong adapter, cardinality refusal, foreign-role Connection refusal, disabled Connection refusal, source-provider/external-ID mismatch and a valid source binding. The last added checks require final build and CI verification. Do not claim the new PostgreSQL assignment tests have run successfully yet.

Pending generated migration: `20261022025345_PinConfiguredLogicViewSelections` and its designer/snapshot. It adds nullable revision provenance and a View FK, with an all-or-none check that explicitly requires non-null positive ViewRevision. No historical applied migration was edited.

Resume by inspecting the actual diff, completing current builds, regenerating OpenAPI for the final unavailable-member selection response, running relevant checks, reviewing transaction rollback/cleanup, and publishing a separate implementation PR against current develop. Run exact-head CI and merge only after all required jobs succeed. Update the main design's old sections 18–19 to reflect delivered behavior and distinguish the current deployment-wide Connection-read permission from future individual-Connection grant administration.

Useful scratch scripts/logs if the same environment reconnects: `/tmp/wsa-view-assignment.py`, `/tmp/wsa-view-assignment-guards.py`, `/tmp/wsa-view-assignment-ui.py`, `/tmp/wsa-assignment-tests.py`, `/tmp/wsa-unavailable-members.py`, `/tmp/wsa-assignment-*.log`, `/tmp/wsa-view-visual.cjs`. These are aids only; the recovered working tree is authoritative.
