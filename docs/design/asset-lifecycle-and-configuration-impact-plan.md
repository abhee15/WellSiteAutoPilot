# Imported Asset lifecycle and configuration impact

Status: implementation plan, 22 October 2026. Reviewed `develop` at
`cdfda4c6f1481e783a297b12423f3c01b67af6fc` (#259). No runtime change is delivered by this document.
This is the next-stage plan for `foresite-discovery-import-refresh-plan.md`; it supersedes recommendations
in that document where they concern the single hierarchy, change acceptance or configuration safety.

## Agreed product contract

One Connection owns one canonical imported hierarchy. A source well has one AutoPilot Asset identity within
that Connection, irrespective of its name, well type, device type or current parent. Views are explicit well
subsets; they neither create another physical hierarchy nor dynamically enroll/remove wells when source facts
change. ForeSite and PetroVisor representations remain different Assets even if their names match.

A source well type such as ESP or RRL is a fact on an Asset whose neutral AutoPilot Asset Type remains `well`.
Changing that fact is not a conversion to a different AutoPilot Asset Type or grounds to replace the Asset ID.
A device-type change may affect both its hierarchy path and its module compatibility.

Changes must be observable and reviewable before updating imported Assets. A confirmed change that could
invalidate configuration must also create durable configuration attention, independent of accepting the
hierarchy change. User confirmation alone cannot override a declared incompatible module requirement.

Deletion/reimport is an occasional recovery operation for truly unused imported leaf wells. Referenced wells
retain identity. Removing an import never deletes or writes to the source system. Empty-group cleanup is
separate and never cascades into wells.

## Current implementation and the gaps that matter

| Current implementation | What can be reused | Gap before lifecycle changes ship |
| --- | --- | --- |
| `SourceBindingEntity`, `ConnectionImportRepository` | Unique Connection/resource-kind/external-ID binding, atomic import and audit | Snapshot contains source metadata only, without an explicit format version, original source name, recipe or field availability. Reimport skips existing bindings; it is not refresh. |
| `ImportPlanner` and group reuse | Deterministic preview, counts, fingerprint, transactional write authority | Group identity is a display-name path. Same labels, fallback labels, truncation and source renames cannot safely identify a group. |
| `DiscoveryResult`, `DiscoverySnapshotStore` | Complete inventory, principal display, user/Connection-bound 15-minute snapshots | Membership completeness is separate from optional-field availability. An ephemeral snapshot cannot be the durable configuration warning. Discovery is not continuous monitoring. |
| ForeSite discovery | Authorized asset filter, scoped wells and control-group lookup | Optional device failure can coexist with a complete well list. Source account/scope changes must not become proof of source deletion. |
| Connection-scoped Views | Explicit membership and revision checks, Connection visibility | Moving a member must preserve membership and re-evaluate authorization. A View's membership is not the authoritative historical target set of a saved configuration. |
| `ConfiguredLogicService`, `ConfiguredLogicRepository.GrantLifecycle` | Immutable revisions, pinned target IDs, adapter/type checks, locking and authority fences | Current target checks enforce active Assets and neutral Asset Type. ESP/RRL/device compatibility is not established by `RequiredAssetTypeKey = well`. `RequiredTraits` is declared/shape-validated, but no target trait evaluator enforces source well/device values. |
| Execution persistence and scheduler | Durable requests, overlap checks, current revision checks and outbox | No source-fact/compatibility revision is currently checked at all dispatch boundaries. A UI flag cannot stop stale queued work. |
| Recommendation decision persistence | Locked recommendation, execution/configuration checks, fresh authority | A pending recommendation can predate a source change; source compatibility must be checked separately before approval and any future physical write. |
| Asset/source foreign keys and scoped access | Restrictive references, stable IDs, grant predicates | Safe deletion needs more than View and active-configuration checks. Historical/configuration/data/security references must also prevent deletion. Moves can change inherited access. |

These are findings from the named code paths, not claims that source refresh, source compatibility holds or
provider command dispatch have already been implemented. Current configured execution remains Shadow.

## Separate observations, acceptance and operational eligibility

Keep three independently revisioned concerns:

1. **Observed source state:** what a successful check reported, under which source identity/scope, and which
   fields were available. Inspection never overwrites the accepted baseline.
2. **Accepted Asset state:** source facts and hierarchy last applied after review, plus explicit local overrides.
   Accepting a move advances this baseline while retaining the Asset ID.
3. **Configuration eligibility:** whether a specific saved configuration revision may use a particular target
   against the current observed source revision. This can remain unresolved after a hierarchy update.

Use separate status dimensions rather than one overloaded `changed` Boolean:

| Dimension | States / meaning |
| --- | --- |
| Observation quality | Complete, partial, failed, stale; availability tracked per field. |
| Source difference | New, changed, not returned, unchanged, comparison unavailable. `Not returned` never means deleted. |
| Hierarchy acceptance | Pending review, applied, deferred, local override retained. |
| Configuration eligibility | Compatible, incompatible, review required; resolved against explicit configuration/source revisions. |

Acknowledge/dismiss records a review choice; it cannot silently advance the accepted source baseline or clear a
configuration hold. A later check, source reversion or hierarchy acceptance cannot silently resume held logic.
Revalidation against the latest source revision is required. Repeated identical observations update last-seen
information without generating duplicate issues or repeated notifications.

## Storage and identity direction

Use the existing PostgreSQL store, current services and transaction conventions. No extra service, rule engine
or automatic hierarchy builder is needed.

- **Connection hierarchy recipe:** one versioned ordered level list per Connection, with stable identity,
  revision, created/updated actor and timestamps. First successful nonempty import establishes it atomically. Later
  batches reuse it. Choosing different levels requires a separately reviewed restructure of existing Assets;
  it must not quietly create mixed recipes under the same Connection. The normal import UI shows the saved
  recipe; ordinary imports cannot silently replace it.
- **Stable groups:** key by Connection, recipe version, parent stable key, dimension and normalized source
  value key. Prefer ForeSite asset/well-type IDs where supplied. Device type currently has a string label, so
  preserve that contract and treat changes explicitly. Distinguish absent values from actual source values
  named `Unknown type` or `Unassigned`. Group labels are display data, not identity. Flat recipes are valid.
- **Accepted source baseline:** explicit format version, original source name/facts, stable source identity,
  field availability, accepted revision and recipe/path provenance. Keep local names/attributes and source
  management ownership explicit. Never infer an original source name from a locally editable Asset name.
- **Source check:** ID, Connection/settings revision, source principal and available scope evidence, start/end
  UTC, completion/field coverage, counts, last successful check and an observation revision. Persist normalized
  observations/change evidence needed for review, not credentials or unlimited raw provider payload history.
- **Change/impact records:** one deduplicated outstanding issue per source identity/change revision and affected
  configuration revision. Record changed facts, observed/accepted versions, target role/input references,
  disposition, resolution reason and actor. An active configuration can be held without rewriting its immutable
  content or losing its active pointer. Historical revisions are shown as history, not running work.

Compare normalized values and stable IDs; ignore JSON property ordering and insignificant whitespace. Retain
field-level availability from both baselines. Failure to retrieve a field is not a confirmed blank value. A
Connection remains the source owner; hierarchy changes cannot transfer wells across Connections.

Legacy imports require a deliberate transition. Metadata-only baselines can support limited fact comparison,
but missing source names, recipes and availability must be labelled unknown. Existing groups must be mapped to
stable keys with reviewed evidence. Audit events may suggest a recipe, but conflicting import histories or
ambiguous same-named groups cannot be resolved by guessing. Unresolved legacy mappings block restructuring,
not basic browsing or historical reads. Take a fresh source snapshot and review adoption of a canonical recipe
without replacing well IDs. Do not silently backfill the recommended hierarchy onto existing imports.

## Check, review, apply and resolve

**Check for changes** performs fresh discovery through the same authorized ForeSite sequence as import. It
compares by Connection/kind/external ID, captures field coverage, and records observations and potential
configuration impacts. It makes no Asset moves, field updates, deletion or View changes. Before persisting an
observation/impact, recheck Connection revision and authority; a check from another user or Connection cannot
be reused. A failed check retains previously unresolved issues. ForeSite continues to use the Gateway Windows service
account, not delegated browser-user identity; display that identity and compare available asset-filter scope
evidence, including the unmapped scope. AutoPilot authorization is enforced independently.

The review shows source/account, check age, coverage, changed facts, accepted/current local/proposed paths and
affected references. Separate current View membership from targets pinned in every saved configuration
revision. Include target bindings and data bindings; a well used solely as an input may still invalidate logic.
Separate active, draft/validated and historical revisions. Exact-scope users see only authorized details;
hidden references must not leak names or IDs. Initial Connection-wide mutation can retain the current broad
Asset-management requirement, but it must state that requirement clearly.

**Apply selected changes** binds the source-check ID, Connection/recipe revisions, accepted binding revisions,
current local Asset state, impact revision and selected actions into a reviewed plan. Revalidate inside one
transaction, in a documented lock order shared with configuration writes/dispatch. Reject stale plans as a
whole. Create/reuse stable groups, update only approved source-managed fields, move existing well IDs, advance
accepted baselines and audit the result. Preserve local overrides, View membership, pinned targets and history.
Retain empty groups by default. A permission or inherited-scope change is part of the impact review, not just
a tree presentation change. No cross-Connection move is allowed.

**Resolve configuration impact** is a separate action with configuration-management authority. Evaluate its
pinned targets and input bindings against the current observed source facts and declared module requirements.
If incompatible, require an appropriate new configuration/module revision or removal of the target through the
normal reviewed revision lifecycle. If requirements cannot establish compatibility, keep `Review required`.
For legacy modules, an authorized engineering attestation may record reviewed compatibility for the exact
module/configuration/source revisions; it must not override a declared failure, grant new scope, or apply to
future source changes. Acknowledging the notification is not such an attestation.

Introduce a small, versioned source-fact requirement contract for modules before advertising automatic ESP or
device compatibility. Define supported operators, stable well-type values, unknown/blank semantics and adapter
scope explicitly; avoid free-form executable expressions. Reuse existing manifest validation/versioning and
add one evaluator reused by setup, revalidation and execution. Neutral `well` type and undeclared traits must
never be interpreted as proof that a source-type change is safe.

## Execution safety boundaries

Persist a per-target eligibility revision/hold, separate from hierarchy acceptance. A verified well/device
change affecting an active configuration places it in `Review required` unless its declared requirements
positively establish compatibility. Unknown compatibility is not a successful validation.

The same gate must cover configuration validation/activation, manual requests, scheduled dispatch, delayed
outbox delivery/worker start, recommendation approval and future Gateway writes. Requests carry the expected
source/eligibility revisions; stale work fails with an actionable reason and cannot be revived by a retry.
Where multiple target/input Assets form one configured execution, hold that complete execution; do not silently
run a smaller subset. Future explicit per-well fan-out is a separate execution contract.

Check the gate transactionally when staging durable execution/approval records, and recheck at the last
supported execution boundary. A source change can occur after staging. In-flight work is marked stale and its
outputs cannot automatically become actionable recommendations or commands. Already completed physical actions
cannot be rolled back by changing database state; retain their audit and reconcile explicitly. Physical write
support remains disabled until its final provider boundary can enforce the eligibility contract; this plan
does not imply that such a provider command path exists today.

Manual discovery cannot detect changes that have not been checked. Display last successful check and freshness
prominently. Define a maximum acceptable source-observation age before enabling any control-capable mode; a
scheduled checker can later maintain freshness. Failed/partial checks preserve existing holds and produce
coverage/freshness attention. They never clear device values, relocate wells, or classify every well as removed.
Do not promise continuous source validation from a manual Check button or perform a full 10,000-well discovery
on every execution. Source account/scope changes require separate review of observation quality.

## Scenario and acceptance matrix

| Scenario | Hierarchy/Asset behavior | Configuration behavior |
| --- | --- | --- |
| ESP becomes RRL | Review fact/path difference; move same well ID after acceptance. | Flag all affected target/input revisions; positively revalidate or hold active work. |
| Device type changes | Review device fact and applicable recipe path. | Re-evaluate device-sensitive requirements, provider input mappings and commands. |
| Source asset changes but group names match | Use source asset ID; distinguish groups and review move. | Review configuration and inherited-access impact. |
| Source rename or group-label rename | Preserve source and Asset identities; respect local name overrides. | Report references; do not infer functional incompatibility from a label alone. |
| Optional device API fails or times out | Device comparison unavailable; preserve accepted fact/path. | Preserve unresolved holds; report coverage/freshness without inventing a type change. |
| New well | Offer explicit import under saved recipe. | No automatic View enrollment or logic assignment. |
| Well not returned or source access reduced | Mark observation inconclusive/not returned with scope context; no automatic removal. | Show affected references and availability concerns; no deletion-based auto-resolution. |
| Same name, different source IDs | Keep separate wells/groups by stable keys. | Resolve targets by stored Asset/source IDs, never name. |
| Source ID appears reused for a different well | Treat suspected identity replacement as a conflict requiring review. | Hold uncertain targets; do not transfer old configuration/history to a guessed replacement. |
| Source changes again during review | Invalidate stale proposal; recheck and review latest values. | A resolution for the old observation cannot clear a newer hold. |
| Source reverts to earlier type | Record the new observation and compare again. | Revalidate; never resume merely because a display path matches again. |
| Well removed from current View but retained in a saved revision | Current View membership changes only by its own edit. | Historical/pinned bindings still count as references and impacts. |
| Reimport existing bound well | Reuse current source binding; direct to change review. | Preserve configuration and Asset identity; no silent baseline acceptance. |
| Delete truly unused imported leaf | Guard and delete Asset plus source binding atomically, with audit. | Refuse if any saved configuration, execution, recommendation or security reference exists. |
| Reimport a safely deleted well | Fresh authorized discovery/review, saved recipe, new AutoPilot ID. | No restored View membership, overrides, grants or assignments. |
| Simultaneous delete/reimport/assignment | Lock and recheck references in one transaction; one consistent result. | No dangling references or assignment to a deleted target. |
| Parent/group cleanup | Empty, unreferenced groups only; no cascade. | Group-scoped security/history references also block cleanup. |
| Move changes descendant authorization | Preview current/proposed scope impact and require adequate authority. | Existing saved targets still undergo fresh execution authorization; Views never confer access. |

## Delivery order and release gates

1. **Canonical identity and baseline foundation.** Persist recipe/revisions and source-baseline format, introduce
   stable group identity and explicit field availability. Provide a reviewed legacy adoption path. Enforce one
   recipe for subsequent import batches before allowing source-driven moves. Test same labels/different IDs,
   fallback collisions, flat recipes, local overrides and rollback against real PostgreSQL.
2. **Durable detection and impact review.** Add manual checks, deduplicated observations/issues, bounded search
   and paging, current/pinned/input reference resolution, permission-filtered details and clear coverage states.
   No hierarchy apply in this slice. If a diagnostic preview ships before the durable gate, label it diagnostic
   and do not claim execution safety. Do not change accepted baselines by running or dismissing a check.
3. **Compatibility contract and execution fence.** Implement the small manifest prerequisite evaluator and
   durable eligibility/hold checks across manual/scheduled/queued execution and recommendation decisions. Test
   superseded observations, permission revocation, retries, in-flight results and the final supported runtime
   boundary. Keep control-capable modes disabled where a final write fence is absent.
4. **Reviewed hierarchy/fact apply and resolution.** Apply selected changes atomically, retaining well IDs and
   independent configuration holds. Test moves concurrent with configuration activation/dispatch, changed
   recipes, revoked authority, lost responses and repeat apply. Hierarchy acceptance must never clear a hold.
5. **Guarded deletion/reimport.** Leaf-only eligibility review and atomic reference checks, including inactive
   and historical records, input bindings and security scopes. Preserve audit, refuse unsafe deletion, and prove
   fresh reimport/idempotency. Empty-group cleanup follows separately.

Each slice is a cohesive PR with relevant pure tests, real PostgreSQL/HTTP tests, API authorization checks,
generated client/build verification and browser interaction/layout checks for its UI. Test the simulator's
source mutations and failures through the Gateway; use large inventories beyond the first discovery page.
On-prem acceptance must verify ForeSite's actual well/device changes, restricted-account behavior and optional
API failures. Simulator tests do not prove Windows delegation or source latency.

Minimum end-to-end proof before calling the lifecycle safe: import an ESP well, assign it through a View,
activate Shadow logic, observe a source type change, retain the same well ID and View membership, surface its
pinned configuration impact, reject stale execution, apply the reviewed move, confirm the hold still exists,
then resolve against the latest facts/configuration revision and permit execution again. Repeat with a failed
device lookup, a source reversion, an input-only reference, a concurrent dispatch and a revoked permission.


## Canonical import foundation checkpoint

The foundation slice persists one recipe per Connection and indexes imported groups by structured stable keys
instead of names. Recipes retain their grouping identity contract; indexed groups retain the accepted source label
separately from the editable display name. Import review uses actual reused group labels. Keys include Connection,
parent, dimension, source ID where available, and an explicit missing
value tag. New source baselines retain format version, original source name/facts, recipe/group keys, field
unavailability and Gateway identity; older metadata-only baselines remain readable without fabricated names.

Discovery reports New, Saved or Legacy hierarchy state. Saved recipes are used by subsequent imports and cannot
be replaced through ordinary import requests. Legacy adoption requires explicit consent and verifies every bound
well's existing ancestry against the proposed levels and stored source facts. It adds identity mappings without
moving/renaming Assets, changing View membership or accepting new source facts. Conflicting paths, cycles, inactive
ancestors, missing source identity evidence and collapsed same-name source groups block adoption. Unused/unverified
legacy groups remain untouched. An adoption-only apply is supported when all discovered wells are already bound.

Recipe establishment/adoption, group keys, new Assets/bindings and audit are committed together with current
permissions and Connection state rechecked. Changed local state invalidates adoption; competing imports cannot
create duplicate well bindings or establish different recipes. Discovery snapshots are tied to the Connection
revision and cannot be imported after its settings/revision change. ForeSite declares optional device unavailability
separately from membership completeness; missing well IDs make membership incomplete. ForeSite null/zero asset
IDs normalize to the same unmapped identity. API-level availability does not prove that an omitted per-well
device value is a confirmed blank; later change review must retain that distinction before clearing facts or
moving wells into an unknown-device group.

Still pending: durable source-check/impact records, the module source-fact evaluator and execution guards,
reviewed source-driven moves or recipe restructuring, and deletion/reimport controls. No execution eligibility
claim is made by the import foundation. Ambiguous legacy layouts need a later explicit repair/restructure path;
this slice preserves them and refuses to guess.
