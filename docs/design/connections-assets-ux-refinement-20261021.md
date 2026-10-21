# Connections and Assets: design refinement review

Date: 2026-10-21
Status: **Design observations and recommendations for owner review; not implemented.**
Ownership decisions: [Connection-owned Assets and Views](connection-owned-assets-and-views-20261021.md).
Visual baseline: retain the previously agreed WellSite AutoPilot prototype direction, existing semantic design tokens and accessible light-capable UI. **Do not invent a dark industrial palette.**

## 1. Current UX findings from develop

Code examined: src/web/src/views/ConnectionsPage.tsx, ConnectionImport.tsx, connections.css, AssetsPage.tsx, AssetTree.tsx, AssetBrowser.tsx, AssetDetail.tsx, assetTree.css, AssetInventoryControls.tsx and assetSavedViews.ts.

| Page/area | Exists now | Gap affecting usability |
| --- | --- | --- |
| Connections list + detail | Two-pane list and connection form with status | Form, test and management all share one long surface; no clear progression through setup versus discovery/import |
| Connections actions | Save, Test, Find, Enable/Disable, Delete in one wrapping .connection-actions row | Primary vs operational vs destructive actions compete; uneven alignment and wrapping; stale/dirty state can be hard to understand |
| Discovery | Search, facets/counts and source account, large-system snapshot | Selection and import context are visually far apart; not enough distinction between "found", "chosen", "to be created", and "already imported" |
| Hierarchy creation | Add/reorder grouping levels, preview and import | Ordered grouping editor and tree preview need clearer affordances, hierarchy indentation, labels, empty-state guidance and visible destination Connection |
| Assets tree | Basic tree with +/- expand buttons and name/count rows | Text-only nodes do not communicate Connection vs Group vs Well vs Facility; there is no Connection root |
| Assets details | Type, status, WSA ID, ancestors, children, logic, runs, recommendations | No first-class Connection identity, source external key, origin and navigation back to Connection |
| Saved Views | Browser-local saved filters under "Filters and saved views" | No top-level Create View; hidden in the >=100 Asset lazy browsing mode; no server-backed grouping or sharing |
| Large Assets tree | Server-paged child expansion and server-side search | Current filters/views disappear at scale. Proposed Connection roots/Views require server-side support, not an in-memory-only decoration |

Existing code was inspected, not visually browser-tested in this docs task. The suggestions below are UX recommendations rather than claims of measured browser defects.

## 2. Desired mental model and information hierarchy

A **Connection** is where a source lives, **Assets** are what was imported or created within that Connection, **Groups** organize them, and a **View** is an alternative way of looking at them without moving or duplicating Assets.

### Proposed Assets tree (illustrative)

~~~text
Assets                                         [Create View] [Add Asset]
  [plug icon] ForeSite - POP             Connection · 120 Assets
       [folder icon] Field A
         [folder icon] ESP Wells
           [well icon] Well A            Well · Active
  [plug icon] PetroVisor - West          Connection · 95 Assets
       [folder icon] Production
         [well icon] Well A              Well · Active
  [layers icon] Standalone               4 Assets
       [well icon] Test Well
~~~

The two "Well A" nodes are **different WSA Assets**, because they belong to different Connections. This is not a duplicate-detection error. Display the Connection name consistently in search results, breadcrumbs and selected Asset details.

### Tree icon and level language

Use one consistent, accessible, token-based icon system; icons help people scan, but labels remain explicit.

| Node | Suggested Lucide icon | Label/behavior |
| --- | --- | --- |
| System-managed Connection root | plug (or cable) | Connection display name, secondary label "Connection"; open Connection details; stable ID available to copy in detail, not a full UUID in each tree row |
| Group/hierarchy level | folder / folder-tree | Actual grouping label (Field, Lease, Well Type etc.) if available, otherwise generic Group; folder reflects expand/collapse |
| Well Asset | droplets | "Well"; WSA ID available in details |
| Facility/other typed Asset | box / boxes | Use Asset type metadata to choose appropriate glyph; default generic asset icon; avoid hardcoding vendor-specific icons into core types |
| Standalone root | layers | Distinguish user-owned standalone Assets from imported ones; no fake Connection ID |
| Saved View | bookmark or panels-top-left | Alternative arrangement; never represents an Asset or persisted Group |

Keep disclosure chevrons separate from node-type icons; don't rely on color to communicate type/state. Align icon columns and tree guides, keep comfortable hit targets, keyboard tree navigation, focus indicators, selected state, responsive overflow and accessible names. Use subdued colors/tokens; show exceptions via readable text/status, not a colored icon alone.

## 3. Connections page refinement

**Keep** the existing two-pane list/detail pattern, but restructure the selected connection into understandable stages:

1. **Connection** — title, adapter label, enabled state, last-tested result and owning service account. Edit name/settings with a coherent **Save changes / Cancel** action row. Keep the adapter immutable as today.
2. **Discover** — one clear "Find assets" action, total found, complete/partial warnings, count facets, server-backed search and selection. State explicitly which Connection is being searched and who the external system saw.
3. **Structure** — selected number, chosen grouping fields, drag/reorder or keyboard Up/Down controls, and a live tree preview **beneath the Connection node**. Offer a sensible simple default, remove/reorder controls and fallback labels without extra jargon.
4. **Review** — counts for new/existing Assets and Groups, a clear separate "already imported" count, permissions/conflicts, snapshot validity, and warning about any incomplete discovery. Preview nothing as "all" if the result is sampled/paged.
5. **Import** — single emphasized confirm action after review (for example "Import 120 Assets"); show progress/result and links to the Connection's Assets. No silent partial application.

**Button placement and hierarchy:**
- Keep the main action in a consistent footer/toolbar region for the active stage. Do not mix Save, Test, Find, Disable and Delete in one wrapping row.
- Connect/save operations live with settings; Test near connection status; Find/Review/Import follow the stage. Disable and Delete are secondary/destructive actions in a separate clearly labeled management section.
- Only one dominant primary action per stage; reserve destructive confirmation for a separate explicit state.
- Responsive layout: controls wrap in intentional rows with stable spacing and touch targets rather than scattered inline buttons. On narrow screens, stack stages and anchor important actions near their content.
- Changes to fields should mark Test/Discover/Review as stale as appropriate; disabling/revision conflict should display an actionable explanation without discarding user-entered text.

**Large-system path:** the server currently supports up to 50,000 found objects with a 15-minute, per-user/Connection snapshot, the first 1,000 listed, and a maximum of 5,000 per import; do not imply all matches are shown. Keep search and facet counts authoritative on the server; show "X found / Y match / Z selected / N new" distinctly. If a snapshot expires, return to Find with a clear explanation; never silently re-import an unreviewed changed selection.

**Import hierarchy preview:** show the Connection root as the top-level anchor even if the selected grouping list is empty (flat import). A preview must clearly distinguish virtual Connection root, planned Groups, imported Assets and potential conflicts. Long trees need incremental/lazy browsing, summaries and an obvious "Review import" boundary. No automatic cross-Connection merge by name.

## 4. Assets page refinement

Proposed structure:
- **Toolbar:** Assets title, always-available Search, visible **Create View**, and context-appropriate Add Asset / Import from Connection entry. Avoid hiding key discovery actions inside collapsed filters.
- **Navigation pane:** Connection-rooted tree by default, with icons, node counts and expand/collapse; optional standalone root. Clear current View selector ("Default hierarchy" vs named View) at the top.
- **Inspector/detail pane:** breadcrumb starting at Connection, node type and Asset type, WSA Asset ID, read-only owning Connection name/ID (with link and copy affordance), provider external key, last known import/source status if the backend can supply it, attributes, logic/run context and relevant actions.
- **Search:** server-side across all authorized Assets, preserving the distinct Connection in each hit and breadcrumb; support search and filtering within a selected Connection.
- **Create Asset:** default to the selected Connection or Group when context is present; label it explicitly. For standalone creation, state that no Connection is attached. Do not let users move Assets across Connections through an ordinary parent picker.
- **Create View:** dedicated flow, not a hidden filter preference. Choose name, Personal/Shared (subject to permissions design), Connections to include, filters, grouping (reorderable), sort and preview; save and appear in selector. View state is server-backed and works beyond 100 Assets.
- **Asset Types:** keep as a separate tab, but ensure asset type labels are not confused with Connection or virtual grouping nodes.
- **Scale:** share the same row/icon/selection primitives between AssetTree and AssetBrowser. Current small-tree and lazy-tree modes must not differ in features merely because the dataset passed the 100-Asset threshold.
- **State:** loading, permission denied, empty selected Connection, disabled source, search no-results, expired import snapshot and view-not-found need intelligible empty states.

### View behavior example

~~~text
View: ESP wells by Field
Scope: ForeSite - POP                    Edit View | Share (if allowed)
  Field A
    Well A
    Well B
  Field B
    Well C

[Switch to Default hierarchy] -> same Asset IDs, no Asset/Group changes.
~~~

## 5. Design system, accessibility and restraint

- Reuse design tokens, spacing scale, typography, selected-row treatment and icon patterns consistent with the agreed prototype. Light/dark adapts through tokens, not a newly invented palette.
- Progressive disclosure: normal happy path should be short; advanced discovery criteria and Connection settings can expand on demand.
- Keep primary actions visually consistent across both pages (position, labels, disabled/loading states, confirmation).
- Icons cannot be the only accessible name. Ensure treeview ARIA keyboard behavior, meaningful focus, screen-reader announced status, no title-only icons, and legible touch targets.
- Show health as "Last tested at …", not "Healthy now" unless actively checked; don't invent real-time source status.
- Avoid an elaborate dashboard, generalized form engine or drag-and-drop hierarchy editor as a prerequisite for first refinement; ship small, tested UX slices using existing controls.

## 6. Suggested future work slices and acceptance checks (not started)

**Slice A — Consistent hierarchy model and tree language.** Define virtual Connection root and ownership fields/guards first; introduce icons and labels in both tree modes; prove duplicate Well A in two Connections remains distinct; show Connection in breadcrumbs/details.

**Slice B — Connections workflow polish.** Stage layout and button alignment, source summary, discover/structure/review/confirm continuity, safe stale handling, small-screen verification and keyboard-accessible level reordering.

**Slice C — Real Views.** Server model and query/permissions; Create View/Preview/Edit/Select/Share flow; replace or transition the browser-local saved filters. Search and grouping work across very large hierarchies without bulk loading.

**Slice D — Contextual refinements.** Source provenance/external keys, connection-level counts, import/result navigation, status and change-checking when supported by backend APIs.

Future testing should include desktop/mobile widths, at least 100+ and 5,000+ Assets, keyboard/assistive access, permissions, identical Asset names in different Connections, empty and disabled Connections, repeated imports, and stale snapshot errors.

## 7. Scope and conflicts

This document is design-only. Nothing here has been implemented by this branch.

The older [Connections workflow](connections-workflow-design.md) recommends one physical Asset across ForeSite and CygNet; that is **superseded** by the owner's explicit single-Connection ownership rule. Keep any previous Views exploration only where it describes virtual saved arrangements, not cross-Connection Asset identity.

Please treat exact layout, icon mapping and detailed sharing rules as proposals requiring review; single Connection ownership, non-overlap, Creation from Connections, visible Connection roots and the need for real Views are the agreed requirements.
