# ForeSite discovery, import and hierarchy refresh

Status: reviewed implementation plan, 22 October 2026. Reference: `poc/foresite-asset-discovery`
at `c48e81c`; current baseline `develop` at `07943fc` (View selection and assignment).
The adapter correction was delivered in #257. The import workflow slice now implements paged inventory,
explicit well selection, coverage counts, hierarchy presets and separate review actions. Reconciliation
and persistent hierarchy identity remain planned; this UI change does not claim source synchronization. This supplements the connections workflow design and supersedes its
initial saved-filter assumptions wherever they affect import.

## Product intent

A Connection brings the source inventory into AutoPilot. Its hierarchy makes wells easy to find.
A View is a named explicit selection of those imported wells for logic assignment. Import does not assign
logic or automatically enroll newly discovered wells in a View. Changing a hierarchy preserves well IDs,
View membership, configuration snapshots and execution history.

The imported tree is an AutoPilot projection built from source facts, not a promise that ForeSite exposes
one canonical tree. ForeSite supplies assets and well/device facts; engineers can choose how to arrange them.
The Connection is already the owner/root in Assets and should not be duplicated as a physical group.

## Findings and reuse

| Current code | Finding | Reuse / adjustment |
| --- | --- | --- |
| POC contracts and recorded checks | User assets precede filtered wells and control-group data. `Wells/All` bypasses scope. | Adopt the verified sequence; retain production token/cookie authentication. Do not merge the older POC wholesale. |
| `ForeSiteAdapter` | Device type relies on `PrincipalDeviceType`, which the list API usually leaves blank. | Enrich by WellId from the filtered control-group response; preserve well-list fallback. |
| `DiscoverySnapshotStore`, `DiscoveryScopes` | Complete inventory can be searched without another source call; sample is capped at 1,000 and facet values at 200. | Keep snapshots/search; distinguish total inventory from sample and import selection. Add searchable/paged facet values for large inventories. |
| `ConnectionsPage.FoundList` | A flat sample and five dropdowns conflate discovering, filtering and selecting. A dimension with one value is hidden. | Show coverage and an inventory explorer; offer filters progressively. Always explain unavailable/unknown device information. |
| `ConnectionImport` and CSS | Grouping starts empty. "Add group level" immediately changes the recipe. Review shares an `align-items:center` row with a label/select. | Replace the ambiguous row with explicit hierarchy setup, preview and separate primary action; align control bottoms and stack on narrow screens. |
| `ImportPlanner` | Pure ordered grouping and deterministic fingerprint already exist. Preview contains counts and sample names. Groups are identified by display-name paths. | Reuse planning/fingerprints/counts, introduce stable group keys before safe hierarchy updates. Same-label groups must not collapse unrelated source identities. |
| `ConnectionImportService` | Bound source keys are skipped. Re-import can add new wells, but never refresh existing well facts, names or positions. | Keep idempotent create; add an explicit reviewed change plan. Do not treat re-import as synchronization. |
| `SourceBindingEntity` | Connection/kind/external ID and import-time metadata snapshot exist. No last-check, observed state or accepted baseline revision. | Preserve identity; add reconciliation state and baseline rather than matching names or replacing Assets. |
| Views/configured logic | Static membership and immutable configuration targets are implemented. | Keep IDs stable; show affected Views/configurations during review. Never silently recalculate membership. |

## 1. Adapter correction

Sequence under the same authenticated session:

1. `GET TokenService/CreateToken`, retaining its cookie and Windows credentials on subsequent calls.
2. `GET Wells/UserAssets` to obtain this calling identity's assets and names.
3. `POST Wells/Well/Filter` with the distinct returned asset IDs plus `0`.
4. `POST WellConfig/GetWellAutonomousControlGroupData` using exactly the same filter.
   Join `Values[].WellId` to well IDs and map `DeviceType` separately from `WellType`.

`0` means Not Mapped. ForeSite applies its own unmapped-well access rules. With no mapped assets, send `[0]`,
never `{}` or an empty AssetIds list, since those can mean all visible wells. Never fall back to `Wells/All`
or `GetWellsByUserAssetIds` (the latter misses unmapped wells). Null/invalid required inventories are failures,
not successful empty discoveries. Reject wells outside the requested asset scope. Deduplicate repeated well IDs.

A 401 refreshes the token once and replays a fresh request with the identical body. A 403 is not retried.
Asset/well failures make discovery incomplete. Optional device enrichment failure keeps wells and emits a
clear warning; absent/blank device types remain unknown. Skip expensive device lookup when no wells returned.
Large responses remain complete even when the UI displays only a sample; do not invent source pagination.

**Identity boundary:** today these requests run as the Integration Gateway's Windows service account, not
the browser user's delegated Windows identity. `UserAssets` enforces that account's ForeSite permissions;
AutoPilot separately enforces its own Connection/Asset permissions. The UI must show "Read as <account>".
If each signed-in user's individual ForeSite entitlements are required, that needs an explicit delegated or
user-bound connection identity design and real Windows tests. This change does not claim impersonation.

## 2. Import workflow after Views

Use an explicit task flow inside the selected Connection:

1. **Discover wells.** Show source/account, discovery time, completeness, total wells, distinct source assets,
   imported/new counts, and known/unknown device counts. These are separate counts with explicit labels.
2. **Review inventory.** A browsable source hierarchy with real well rows, scoped search, branch expansion,
   optional filters and a visible selection summary. Reuse browser/selection presentation, but use
   `(ConnectionId, resource kind, external ID)` for pre-import selection, not fabricated Asset IDs.
3. **Choose hierarchy.** A recommended starting recipe is ForeSite asset > Well type > Device type > Wells.
   Offer recognizable presets (by source asset, by well/device type, flat wells) and optional customization.
   Display the ordered path persistently, with example groups and counts below it. "Add grouping level" has a
   label, description and explicit action. Grouping choices organize the imported tree; filters choose which
   wells are imported. Do not place these controls together without explaining the difference.
4. **Review changes.** Display wells/groups to create, groups reused, already-imported wells, unknown facts,
   conflicts and destination. Keep "Review import" in a separate action area. The primary action states what
   will happen, e.g. "Import 250 wells". Never call a well count an asset/group count.
5. **Done.** Open the imported Connection hierarchy or create a View with that Connection selected. Creating
   a View still requires choosing its explicit well membership.

Device-type coverage must remain visible even if every value is unknown or there is only one known value.
Explain unknown versus failed retrieval. If enrichment failed, do not apply a device-based hierarchy as though
unknown were a confirmed source change: offer retry or an explicitly simpler hierarchy. Keep unknown groups
when the source successfully reports blank device types.

The explorer must page/search the full snapshot, not only its 1,000-row sample. Selection persists across
search/pages. Show when facet values are truncated and provide value search. In v1 keep the existing
5,000-well atomic-import limit, display it before review, and support explicit batches with remaining counts;
do not silently import only a sample. A larger durable background import can be a later measured change.

## 3. Source changes and reconciliation

Start with a manual **Check for changes** action on an imported Connection. It performs fresh authorized
discovery, compares it with accepted source bindings and the saved hierarchy recipe, and writes no Asset
changes during inspection. Scheduled detection may later propose changes; it must not silently apply them.

| Observation | Review behavior | Apply behavior |
| --- | --- | --- |
| New well | Show identity, source facts, proposed destination and import status. | Import selected new wells once under the saved recipe; no automatic View membership. |
| Name or mapped facts changed | Show accepted/current/local values and field availability. | Update selected source-owned fields; preserve local-only attributes and explicitly kept local names. |
| Asset, well type or device group changed | Show current and proposed path and affected Views/configurations. | Move the existing well ID after review; create/reuse stable groups. No duplicate well. |
| Previously imported well not returned | Label "Not returned by source"; access reduction, removal and source failures may be indistinguishable. | No deletion, hiding, deactivation or detachment by default. Explicit follow-up action only. |
| Returned again | Compare using its original source identity, including inactive bindings. | Reuse the same Asset; do not recreate or silently reactivate. |
| Partial/failing discovery or changed source access | Mark check inconclusive with source/account/coverage. | No missing-state updates or destructive proposals from a partial sample. |
| Optional device data unavailable | Mark device comparison unavailable. | Preserve previous accepted device value and placement; never clear data because enrichment failed. |

Identity remains `(ConnectionId, ResourceKind, ExternalId)`. Compare normalized source-owned fields; avoid
false changes from JSON property order, whitespace or missing optional enrichment. Keep separate *observed*
and *accepted* baselines so dismissing/deferring a change does not silently accept it. Track first/last observed,
last successful check, unavailable fields, missing-since observation, and a revision for stale-plan rejection.

Store a versioned hierarchy recipe with the Connection's imported projection. Group identity must include
Connection, parent key, dimension and source value key: ForeSite asset ID, well type ID, and device string
(the device endpoint supplies no device ID). Keep fallback keys distinct from real source labels.
Existing groups contain names rather than these keys, so backfill must be a reviewed mapping; unresolved
legacy paths remain untouched. Changing a recipe is a separate reviewed restructuring operation.

A reviewed plan binds source snapshot/coverage, Connection revision, recipe revision, accepted binding
revisions and current local Asset state. Apply revalidates these with current permissions in one transaction,
uses stable IDs, rejects stale plans atomically, and audits selected counts/changes. Empty groups are retained
by default; explicit cleanup only when unreferenced and safe. Preserve local Asset names/placements unless
the user chooses source-managed updates; record ownership and local overrides explicitly.

`DiscoveryResult.Complete` currently describes membership, while an optional-device warning can coexist
with it. Reconciliation needs field-level availability and source-scope/account context; a single Boolean
cannot justify clearing attributes or inferring deletion. Until that contract exists, any warning must make
missing detection conservative. Even a complete response cannot prove deletion after a source access change.

## 4. Delivery and verification

1. **Adapter correction:** scoped sequence, control-group enrichment, authenticated POST retry, required-shape
   validation; recorded-shape and 10,000-well tests plus simulator through the Gateway. No UI redesign in this slice.
2. **Import UX:** inventory/coverage summary, explicit hierarchy presets/path preview, understandable selection,
   separate review action, responsive alignment. Keep current atomic import backend and fingerprint checks.
   Verify unknown device values, failed enrichment, counts, all paging, cancellation and stale snapshots.
3. **Reconciliation foundation:** persist recipes/stable group identity and binding baselines; readonly diff and
   coverage-aware status first. Test same-name/different-ID assets, changed scope, optional-field failure,
   unchanged results, renames, moved wells and reappearing wells using real PostgreSQL.
4. **Reviewed apply:** add selected wells and update source-managed facts/paths without changing IDs, permissions,
   Views or pinned configuration targets. Test recheck-after-review, idempotency, revocation, transaction rollback,
   cross-Connection refusal and exact audit outcomes before enabling writes.

CI uses contract-shaped fixtures and the simulator, not an assumed reachable on-prem ForeSite. The POC reports
validation against ForeSite 5.7.1 with 1,403 wells; that is reference evidence, not a new live validation by this
change. On-prem acceptance must include a restricted account, no-mapped-assets account, denied unmapped access,
known device values and cold-cache control-group latency at a large site. Record calling identity and counts.
Do not claim real Windows authentication/delegation or source performance is proven by Linux simulator checks.

## Import workflow implementation checkpoint

- Inventory precedes hierarchy setup. Users can import all matches or choose explicit well rows. Explicit
  selections carry resource kind and external ID from the Connection/user-bound snapshot, survive pages and
  searches, and are server-validated. Empty explicit selections do not become an all-well filter; duplicates,
  unknown identities and mixed explicit/filter requests are rejected.
- Search accepts bounded offset/limit and uses deterministic name/kind/ID order. The UI pages 100 wells from
  the full snapshot, including wells beyond the initial 1,000-row sample. Source asset sections are explicitly
  labelled as the groups on the current page, rather than claiming complete source subtrees.
- Device availability counts use the complete snapshot and remain visible when all values are unknown or
  only one device value exists. The mapped source asset count represents IDs attached to returned wells,
  excluding Not Mapped; it is not a count of empty source assets or of generated hierarchy groups.
- Recommended and alternative hierarchy presets expose the path including its virtual Connection root and
  terminal wells. Customization stages a dimension before an explicit Add action; review has a separate
  action area. Import confirmation reports well and group counts separately.
- Pending/failed searches and unapplied search text invalidate review. No successful discovery/selection means
  no UI import review, and batches above the existing 5,000-well limit are blocked before review. The existing
  server fingerprint and transactional import authority remain mandatory.
- Still pending: durable source-change comparison/apply, persisted hierarchy recipes and stable group keys,
  and a dedicated value-search API for facets with more than 200 values. Well search and paging cover the
  entire kept inventory; the existing facet-value limit is shown honestly rather than treated as exhaustive.
