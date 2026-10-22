# Connection-scoped Views for logic targeting

Status: implementation plan, 21 October 2026. Reviewed develop at `813cf2f20cbdaf6fc294eb3dd2ef8819a792a1f7`.
This plan supersedes sections 18 and 19 of connections-workflow-design.md for the meaning, membership,
visibility and editing of Views. Those sections describe the earlier saved-query implementation.
No runtime changes are made by this document.

## 1. Product contract

A View is a named, explicitly selected subset of execution-target Assets belonging to exactly one Connection.
For ForeSite, those targets are wells. A hierarchy is the means of finding targets; its parent groups are
navigation, not execution targets. Views never copy or move Assets.

- Pick one Connection, browse its imported hierarchy, check wells, review the selection and save a name.
- Select wells from multiple branches of that same Connection. A well may belong to multiple Views.
- All users authorized to view that Connection can discover its Views. Asset restrictions still apply to
  member details. No personal/shared toggle, publishing step or creator-only visibility.
- Creation, editing and deletion require their own permissions plus access to the Connection.
  Record creator/updater for audit; creator identity does not determine editing rights.
- Assign a View to a compatible logic module through the existing configuration lifecycle.
  A View grants neither execution permission nor access to another Connection.
- ForeSite and PetroVisor representations of the same named well remain distinct Assets.

Working defaults for implementation: static explicit membership; checkboxes first; per-connection unique names.
Changes to an active assignment's targets require a reviewed configuration revision. Automatic membership
rules and drag/drop are future options, not prerequisites.

## 2. What the current code provides

| Area inspected | Finding | Reuse / change |
| --- | --- | --- |
| Asset ownership and source bindings (#234) | Assets have OwnerConnectionId; imports preserve source identities and isolate groups by Connection | Keep ownership, idempotent imports and external IDs |
| AssetBrowser.tsx, assetBrowseApi.ts (#200, #235) | Paged children, expand/collapse, search paths, owner roots, retry/cancellation | Extend with a fixed Connection scope and well selection; keep lazy loading |
| AssetViewEditor.tsx, assetViewsApi.ts (#239) | Definition stores Connections[], type/status/name filters and three display-grouping dimensions; no selected-member field | Replace editor body and contract; retain form styling, localization and error handling |
| AssetViews.cs, AssetViewRepository.cs (#238, #247) | Personal/shared owner-based reads and writes; revision check and row locking | Reuse identity/revision/transaction conventions; replace visibility and membership rules |
| AssetRepository.Views.cs | Evaluates a browsing query over visible Assets | Retain authorized-query patterns; replace execution membership evaluation |
| Program.cs, AssetViewViewer.cs | View writes use AssetsRead; shared definitions are redacted after owner/shared lookup | Add mutation permissions and Connection authorization before metadata or members are returned |
| ConnectionRepository.cs, AccessGrant.cs | Connection reads are not subject-scoped; scope kinds are ExactAssets, DeploymentWide and AllAssets | Add explicit Connection scope support; global connections.read alone cannot distinguish two Connections |
| ConfiguredLogicSetupPanel.tsx | Asset multiselect uses the loaded assets prop; no View selection | Add server-resolved View selection; do not copy the loaded-list limitation |
| ConfiguredLogicService.cs and ConfiguredLogicRevision.cs | Explicit role/Asset bindings, Draft revisions, validation/activation and cardinality checks | Reuse lifecycle and bindings; add View provenance and resolve membership on the server |
| Import-to-View entry point (#246) | Preselects a Connection for the saved-query editor | Keep entry point, open the new selector with no implicit whole-Connection selection |

The drift began when View was defined as a saved display arrangement. More grouping and sharing features
then reinforced that definition. The correction should change that contract before extending the UI further.

## 3. Storage and API direction

Reuse asset_views as the View header: ID, required ConnectionId, name/normalized name, revision,
creator/updater and timestamps. Use a membership table with a unique (ViewId, AssetId) key, indexed for
paged member queries. Replace personal/shared and per-person/global shared-name indexes with
(ConnectionId, NormalizedName) uniqueness. A saved View's Connection is immutable; choosing another
Connection in a new draft clears its selection.

Keep CRUD route names where practical, regenerate contracts/client from the API, and add paged members
and a server-side selection preview/resolver. Do not return every selected row with each header request.
Save membership changes atomically with expectedRevision; validate every submitted ID, its owner, target
eligibility and the caller's authority. Reject foreign, unknown or ineligible IDs without partial saves.

Selection behavior:
- Wells have checkboxes. Group rows expand; an optional explicit "Select wells under this group" action
  resolves eligible descendant wells on the server, including unopened/paged descendants.
- Return only IDs/lightweight data needed for selection; page names/details. Keep the selection keyed by
  Asset ID across paging, search and branch expansion. A repeated selection never duplicates a member.
- Connection and authorization filters run before paging, counting and descendant resolution.
- Reuse existing well Asset type key/source resource kind as the initial target eligibility rule;
  never use display name, icon or leaf position as authority. Extend to other target kinds through explicit
  type/trait metadata when needed.
- Keep search as a selection aid. Existing source hierarchy already represents asset/well/device grouping;
  arbitrary attribute-query builders are unnecessary for this workflow.
- Save explicit IDs, not a parent or search expression. Newly imported wells are not automatically added.
- Missing/inactive members need a visible review state. Do not silently replace them or run inactive wells.
  A user with restricted member visibility cannot overwrite hidden members through a partial replacement.

## 4. Connection access and View permissions

Introduce a first-class Connection grant scope using the existing users/Windows groups/custom-role system.
Keep existing deployment-wide grants as an explicit broad-access path. Use one shared authorization predicate
for Connection detail/list, owning-Connection visibility and View APIs; do not infer access only from
/assets/owners, which is derived from visible Assets and omits Connections without visible Assets.
Keep role and scope bound to the same grant, as required by the current grant model.

No new standalone sharing ACL is needed. List/detail/member/search/count routes must all enforce Connection
access; knowing a View UUID must not expose it. Revocation removes access on subsequent requests.
Use assets.read for member details alongside existing Asset scope checks.

Proposed mutation permissions: asset-views.create, asset-views.edit and asset-views.delete.
Seed Admin/Engineer defaults deliberately through the role catalog and migration; custom roles remain
editable. Operators with Connection read access can see Views without mutation rights. Granting edit applies
to Views within the authorized Connection, regardless of creator. Writes require an auditable bound actor and
transactional authorization checks, following the existing Connection write-guard pattern.

## 5. Assigning logic and execution

Add a View chooser to the target step. Resolve View ID plus expected revision on the server and present
the target count, module compatibility, missing bindings and any inaccessible/inactive members before saving.
Never silently assign only the caller-visible subset of a larger View.

Preserve module type/trait requirements and input/output capabilities. Current module requirements do not
declare a provider/adapter restriction, so add the smallest explicit compatibility metadata and validator needed
for a ForeSite-only module. Connection routing comes from the stored View Connection and source bindings,
not a free-form provider text field. Reuse source external IDs; signal/command mappings still need validation
and must not be guessed from hierarchy labels.

Store View ID and revision as assignment provenance and snapshot explicit bindings into an immutable
configuration revision. Editing a View marks affected assignments as having a newer selection available;
the user reviews and applies it through a new configuration revision. Deleting a referenced View is refused.
Runs keep their historical bindings and provenance.

A list of 500 wells must not automatically be passed to a module whose role maximum is one:
- For a one-well module, plan bounded, independently observable per-well execution using the existing execution
  pipeline, with durable/idempotent dispatch and per-well status.
- For a module deliberately accepting a set, retain its declared role cardinality.
- Verify scheduler/worker and request-contract support before claiming fan-out is implemented. Do not raise
  manifest limits to disguise the distinction. Test the complete selected-View-to-per-well-run path.

Existing access, license, activation and execution-mode checks remain authoritative. Saving a View never
starts logic. Disabled Connections remain browseable to authorized readers but cannot be used to start runs.

## 6. Delivery order

1. **Contract, Connection scope and permissions.** Add Connection grant support and common authorized reads,
   permissions, membership schema/API and migration handling. Server tests cover cross-Connection refusal,
   revocation, multi-group roles, stale writes and two different users reading/editing the same View.
2. **Selection UI.** Single Connection selector, reusable lazy tree with well checkboxes, selected-wells panel,
   remove/clear, paged search and server-resolved branch selection. Replace personal/shared controls.
   Retain post-import entry point. End this slice with create/edit/read working for a 10,000-well fixture.
3. **Assignment and compatibility.** View chooser, server resolution, provider/type/capability checks, source
   bindings and pinned configuration revision. Review impact of View edits and forbid destructive deletion
   while referenced.
4. **Execution completion and cleanup.** Complete per-well dispatch where required; verify schedules/manual
   Shadow runs, deduplication and bounded concurrency. Remove superseded query/share/copy routes, obsolete
   permissions, UI/localization and tests after replacements land; update the main design sections.

Keep PRs cohesive and merge only after exact-head required CI passes. No UI-only shortcut that leaves writes
on AssetsRead or assignments dependent on the first workspace snapshot.

## 7. Existing Views and rollout

Inventory existing rows and references before migrating. Old queries do not prove which wells anyone intended
to select; do not automatically turn them into executable memberships or broaden personal visibility.
Preserve legacy definitions in a non-executable archive/export for deliberate manual recreation, with a clear
migration notice. Only the newly reviewed membership participates in Connection-visible executable Views.
Do not copy creator-only audit data into public Connection metadata.

Since the application is pre-release, prefer a simple one-time upgrade path over maintaining two active View
systems. Generate migrations with the supported tooling; test the upgrade on populated legacy personal,
shared, multi-Connection and standalone fixtures. Do not edit previously applied migration files or reset
unrelated Assets/Connections.

## 8. Acceptance evidence

- A ForeSite fixture with 10,000 wells across several hierarchy levels; selected wells include IDs beyond the
  first 100/1,000. Branch selection includes unloaded descendants; filtering/collapse/paging preserves selection.
- Exactly the checked, unique wells persist. Parent nodes, another Connection's wells and non-well leaves are
  refused. Name collisions are scoped per Connection; concurrent updates return a stale-revision conflict.
- Two readers of Connection A see its Views; a reader of B cannot discover A's View, metadata, members or counts.
  A permitted non-creator edits; a read-only user cannot mutate. Restricted Asset visibility cannot erase hidden
  membership. Repeat these cases through direct API calls and group grants.
- View assignment resolves all members server-side; incompatible module/provider, missing mapping, disabled
  Connection and stale View revision are explicit failures.
- A one-well module runs independently for each selected well, with no parent/group targets, no cross-Connection
  routing and no duplicate run on request replay. An edit to a View cannot change an active revision silently.
- Test local APIs/persistence and browser flows using representative mock adapter data. A live on-prem ForeSite
  smoke test separately verifies identity discovery/mapping/read/write; mocks alone do not prove that integration.
