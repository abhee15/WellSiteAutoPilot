# Connection-owned Assets and server-backed Views

Date: 2026-10-21
Status: **Owner-agreed product decisions; design for later implementation. Documentation only.**
Related: [Connections workflow](connections-workflow-design.md), [Connections / Assets UX review](connections-assets-ux-refinement-20261021.md), [UX plan](ux-plan.md).

## 1. Why this note exists

Following the October 21 owner discussion, **Assets from different connections are distinct WSA Assets, even if they have identical names or appear to describe the same physical well.** An Asset cannot belong to multiple Connections. Assets can be created by importing from a Connection. The normal Assets browser should expose a **Connection node** above the imported hierarchy.

This explicitly **supersedes** decision D10 and the cross-connection linking/multiple-source-binding suggestions in sections 6, 7a, 9.4 and 13 of the older [Connections workflow](connections-workflow-design.md). Those passages are historical discussion, **not current product direction**. Do not implement ForeSite-to-CygNet/PetroVisor Asset linking or silently coalesce two connections' Assets by well name.

## 2. Agreed identity and ownership rules

1. **At most one owning Connection per Asset.** An Asset imported from, or explicitly created within, a Connection has that Connection's immutable identity as its owner. The Connection is identified by its WSA Connection ID, not an adapter type, display name, endpoint, or well name. An Asset from ForeSite POP and one from PetroVisor West are *two* Assets even if both display "Well A".
2. **Creation from Connections is a first-class flow.** Discover -> select -> structure -> review -> import creates Assets owned by the selected Connection. A later import into that Connection skips or updates its *own* previously bound objects according to the reviewed workflow; it never attaches them to another Connection.
3. **Manually created Assets can be standalone.** An Asset created outside a Connection may have no Connection. When creating beneath a Connection-owned Group, ownership must remain with that Connection. Whether standalone Assets may later be assigned to a Connection is a separate explicit, auditable operation, not implicit name matching.
4. **Group ownership is also connection-scoped.** Every persisted Group within a Connection's import hierarchy belongs to the same Connection. Two imports from different Connections must never reuse the same Group, even if their parent/name/type appear the same.
5. **No automatic cross-connection merging or movement.** Reparenting an Asset/Group under another Connection is prohibited by default. Any future transfer/migration design requires an explicit audited action and impact review; it is *not* an ordinary tree drag/drop.
6. **Asset names are display names, not identity.** Names can repeat within a Connection where provider IDs differ and can freely repeat across Connections. Stable local Asset IDs and connection-scoped external IDs determine identity.
7. **Keep one logical object per source object within a Connection.** The existing binding uniqueness (Connection + resource kind + external ID) remains useful for idempotence. An Asset must not have bindings belonging to a second Connection. Whether multiple keys from the *same* Connection can attach to one Asset is an implementation detail to specify separately; it must not defeat the single-owner rule.
8. **Connection disable/delete behavior remains safe.** Disabling a Connection does not discard its Assets. Deletion is blocked while it owns imported Assets/bindings unless a future explicit, safe disposition workflow is designed. Renaming a Connection updates the displayed root label without replacing Assets or changing IDs.

### Concrete example

~~~text
Assets
  [Connection] ForeSite - POP                (Connection ID: F)
    [Group] Field A
      [Well] Well A                          (WSA Asset ID: 101; owner F)
  [Connection] PetroVisor - West             (Connection ID: P)
    [Group] Production
      [Well] Well A                          (WSA Asset ID: 202; owner P)
  [Standalone]                               (no owning Connection)
    [Well] Test Well
~~~

The two "Well A" entries must remain separate in search, views, imports, logic associations, permissions and audit. Even if an operator knows they correspond to the same real-world object, WSA does **not** merge them.

## 3. Connection node semantics

**Agreed user-facing behavior:** one clearly identified Connection node per visible Connection in the normal Asset hierarchy. Imported hierarchy levels and Assets appear beneath it. The user can expand/collapse the node, see counts and status, and open the corresponding Connection record.

**Recommended implementation, subject to review:** make this a **system-generated virtual root backed by the Connection record**, not a normal user-created Asset or ordinary Group. Do not inflate Asset counts or allow deletion/renaming through generic Asset actions. Server-side listing/search/ancestors must consistently surface the root and support lazy browsing at large scale. Assets and persisted Groups still need an enforceable Connection owner or equivalent reliably constrained schema relationship; a virtual root alone does not enforce ownership.

Show standalone Assets under a separate virtual **Standalone** root (not "Unassigned", which already names missing grouping facts in import). Connection nodes and standalone nodes must respect existing RBAC; a Connection label, endpoint, or total must not leak to someone lacking appropriate access.

### Connection-root name and hierarchy ordering (agreed 2026-10-21)

- The **Connection node is the top-level root** for imported Assets in the default WSA Asset hierarchy and in import previews. Do not assume Well/Lift Type is the first level.
- **Use the user-selected Connection name as its root label**, not a second independently managed "root Group name". Connection setup may **suggest** a friendly default with an optional provider prefix/short identifier such as `FS-001 · ForeSite POP`; the person can name it `Houston Production` or otherwise rename it to their liking. The prefix is a **display suggestion only**, not the canonical Connection ID or an immutable source key, and must still pass Connection-name validation.
- The underlying WSA Connection ID remains the stable key for ownership, bindings, permissions and links. Renaming the Connection changes only the displayed root label; it does not rename Groups or reparent Assets.
- The hierarchy **below** the Connection follows the selected provider grouping/template. For ForeSite, a natural example is **Connection -> ForeSite Asset -> Lift/Well Type (e.g. ESP) -> Well**. Lift Type is a *lower grouping level after the source Asset*, not an assumed root. Other providers may use different levels or omit Lift Type entirely. Users retain their grouping choices, including a flat import under the Connection node.
- Do not confuse ForeSite's **source "Asset" grouping field** with the WSA `Asset Type` catalog or the unique WSA Asset ID. Icons, subtitles and preview headings should make the level's meaning clear.

~~~text
Assets
  [Connection] FS-001 · ForeSite POP   (user-editable Connection name; ID: F)
    [Group: Source Asset] Asset A
      [Group: Lift Type] ESP
        [Well] Well A                 (WSA Asset ID: 101; owner F)
  [Connection] Houston Production     (custom Connection name; ID: P)
    [Group] Production
      [Well] Well A                   (WSA Asset ID: 202; owner P)
~~~

The same name "Well A" can appear in both Connections without implying any relationship or merge.

## 4. Data model and import implications (implementation proposal)

- Consider adding an optional owning Connection ID to persisted Assets (including Groups), with a foreign key and appropriate indexes. Enforce one owner in the domain/service/repository boundary and database where possible.
- Keep source binding keys unique by Connection + kind + external ID. Validate that every source binding's Connection equals the owning Asset's Connection; reject any second-Connection bind. Review existing rows and migration/backfill behavior rather than silently assigning or merging.
- Scope Group reuse by Connection ID **and** parent, name and Group type (the current importer only reuses by parent/name/type). A Group never hosts children from two Connections.
- The import preview must show a distinct Connection root, counts for new/existing Groups and Assets, already-imported items, conflicts, and the *destination Connection*; the final import repeats validation transactionally.
- Imports must continue respecting complete/partial discovery, selected snapshot and selection scope, idempotence, grants, and existing large-system limits. Preserve the current check-before-apply discipline.
- Manual Asset creation, root creation, bulk import, hierarchy edits, asset-copy workflows, search, authorization and logic configuration must all preserve the owner boundary. Never infer ownership solely from an Asset's display path in the browser.
- Keep neutral Asset types (Well, Group, etc.). A Connection is not a user-defined Asset type.

## 5. Views: distinct from current saved filters

The user has not found a meaningful **Create View** capability because it does not yet exist in the intended form.

**Implemented baseline (as inspected 2026-10-21):**
- [PR #63](https://github.com/abhee15/WellSiteAutoPilot/pull/63) added save/select/delete/reset for named **browser-local filter preferences** (up to 20 per authenticated user, scoped to browser storage). These are not server-side View definitions or custom grouped hierarchies.
- [PR #200](https://github.com/abhee15/WellSiteAutoPilot/pull/200) hides that filter/saved-view panel once the Assets page switches to lazy server browsing at the 100-Asset snapshot threshold, because applying client-only filters to a partial slice would be misleading.
- The Connections flow supports selectable grouping **during import**, but that produces the persisted hierarchy, not saved alternative Views.

**Agreed product goal:** a discoverable **Create View** action under Assets that can save and reopen named, server-backed, dynamically evaluated arrangements. Users choose one or more Connections (where authorized), Asset types/attributes/status filters, ordering, and optional ordered grouping levels; the Assets are not copied or reparented.

Examples:
- "ForeSite ESP wells": Connection = ForeSite POP, type = Well, wellType = ESP, group by Field -> Lease.
- "PetroVisor wells": Connection = PetroVisor West, group by provider-declared field where available.
- "All wells": both Connections selected; two equal names appear as distinct rows, with Connection shown.

Suggested rules for review:
- Personal Views are visible to the creator; Shared Views require explicit sharing permissions/governance to be designed. Views are persisted on the Server and resolved through server-side authorization, filtering and pagination. A View never grants Asset or Connection access.
- Saving a View must not create Asset/Group nodes. It is a presentation/query definition; the canonical Connection hierarchy remains unchanged.
- Grouping can use supported Asset attributes plus *safe, available* source facts; do not invent fields that an adapter does not supply. Empty values get clear fallback labels. If a View combines Connections, include enough Connection context to distinguish duplicates.
- Define stable sorting, pagination, total counts, saved definition versioning, and how Views react when a Connection is disabled or a source value changes.
- Using a View for configured-logic target selection is **future design**, not an automatic grant or runtime binding. Resolve and authorize each Asset explicitly in whatever execution model is agreed later.
- Old browser-local saved filters should be migrated or clearly distinguished rather than silently presenting them as full Views.

## 6. Verification criteria for future work

1. Import "Well A" from Connection F, then "Well A" from Connection P: both visible, distinct local Asset IDs, distinct owners, no cross-link; each still appears when filtered to its own Connection.
2. Re-import F with the same external IDs: idempotent within F only; must not touch P.
3. Import matching Group names from F/P: no cross-Connection Group reuse; all descendants stay in their owner's subtree.
4. Attempt to bind/reparent F Asset to P, including via direct API requests: rejected and audited; UI does not offer unsafe operation.
5. Saved View crossing F/P shows both entries distinctly without creating duplicates; user with only F Asset access sees only F.
6. Large tree lazy browsing, search, drill-down and direct links preserve visible Connection roots and counts without loading all Assets.
7. Connection renamed/disabled: visible root behaves predictably; underlying Asset identity and history do not change.
8. Standalone Asset creation remains possible and cannot accidentally adopt ownership through similar names.
9. Existing RBAC/asset-scope rules continue to apply; mere permission to read a Connection does not reveal unauthorized Assets.

## 7. Next steps (not authorized as implementation by this docs branch)

1. Agree detailed schema, standalone assignment and grouping invariants; identify current data/contract changes.
2. Make a small design review for virtual root and both Asset browsing modes, then implement server/API guards and tests before exposing UX.
3. Build the server-backed Views model, query, permission model and dedicated Create View experience.
4. Apply the related UX refinements in [Connections / Assets UX review](connections-assets-ux-refinement-20261021.md) in independently reviewable slices.

**Scope:** documentation only. No runtime behavior, migration, UI or tests are changed here.
