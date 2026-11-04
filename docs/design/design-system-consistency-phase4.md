# Phase 4 — Cross-screen design-system consistency audit

Status: **Phase 4 inventory, P4-A merged, P4-B selection consistency implemented on a separate branch.** Baseline `develop` on 2026-11-04.
Related: [UI conventions](ui-conventions.md), [Phase 1 tab contract](tab-navigation-design-system-phase1.md),
[Control UI audit](control-ui-audit.md), [Asset identity presentation audit](asset-identity-presentation-audit.md).
Tracking: [#444](https://github.com/abhee15/WellSiteAutoPilot/issues/444).
This is a source/behavior audit, **not** proof of screenshot parity. Visual capture is advisory until [#438](https://github.com/abhee15/WellSiteAutoPilot/issues/438) is stabilized.

## Decision: one owner per visual pattern

The app already has useful primitives (`PageHeader`, `PanelHeader`, `ActionButton`,
`Status`, `FilterToolbar`, `PageTabNav`, `SectionTabs` and `SummaryCard`).
Improve their slots and contracts rather than copying their CSS or adding another component library.
Preserve semantics: URL navigation uses links, real detail tabs use tabpanels, row selection uses `data-selected`,
a tree uses `aria-selected`, and an actionable Connection uses `aria-current`.
These are different interaction contracts; do **not** blindly replace one with another.

## Findings and ranked work

| Area | Evidence in source | Decision / status |
| --- | --- | --- |
| **P4-A, panel headers** | `PanelHeader` in `src/web/src/components/common.tsx` accepted only a title/count. `ConnectionsPage`, `RoleCatalogPage`, `GrantAdministrationPage` and `OverviewPage` recreated `panel-header` markup for right-hand actions; Roles also used bespoke `role-actions` CSS. | **Implemented in first Phase 4 slice.** Optional shared actions slot and H2/H3 semantic heading level. Shared wrapping/gap styles and regression tests. Replace duplicated markup in four areas, including Role editor and list. |
| **P4-B, selection treatment** | `OperationsPage`, `LogicPage`, `RecommendationsPage`, `ActivityPage`, and `RoleCatalogPage` mark selected table rows with `data-selected`. Equivalent token rules were repeated in `splitLayout.css`, `assetPage.css`, `assetTree.css`, and `accessManagement.css`; Connections used the hover-colored surface for the current record. Some record-opening buttons lacked a programmatically announced current state. | **P4-B implementation:** `components/selectedRecord.css` is the common visual owner for these records, including Connection rows. Selection is visibly distinct from hover, while native `:focus-visible` remains separate. The Operations record-opening button now exposes `aria-current="true"` only for its selected record. The existing Asset tree retains `aria-selected` on `treeitem`; tabs retain `aria-selected`, page links retain `aria-current="page"`. Interaction and consistency tests guard these decisions. Do not modify the active Logic Activity PR #442. |
| **P4-C, form controls** | `ConnectionsPage`, `RoleCatalogPage`, `GrantAdministrationPage`, `ConfiguredLogicSetupPanel`, and control screens render their own labeled inputs/selects/checks. `styles.css` contains page-specific form-label selectors. | **Owned by existing [Control UI audit](control-ui-audit.md) / #418, D1.** Establish shared accessible `TextField`, `TextArea`, numeric, checkbox, select, group and action components **once**, then migrate pages incrementally. Do not introduce a second competing form kit in Phase 4. |
| **P4-C, notices/confirmations** | `RecommendationsPage`, `ConnectionsPage`, `RoleCatalogPage`, `GrantAdministrationPage`, `ActivityPage`, and `App` use several combinations of `role="alert"`, `role="status"`, `status-error`, `execution-detail-error`, `access-intro` and one-off note styles. | **Owned by D1/D4 of [Control UI audit](control-ui-audit.md).** Build one role-safe notice/confirmation primitive, then migrate. Preserve persistent warnings, screen-reader urgency, and retry semantics; don't sweep them into a generic toast. |
| **P4-D, other headers** | `PageHeader` centrally controls section h1, live status and refresh; it is already the correct owner. `RecommendationsPage` still needs an action group with timestamp; `GrantAdministrationPage` has nested review headings. | Continue deliberate migration after control UI decisions. Do not duplicate `PageHeader` or move responsibility for refresh/live status to each page. Leave existing control screen markup untouched until its planned redesign. |
| **P4-D, action consistency** | `ActionButton` already governs ordinary actions, icons, busy/disabled states. `FilterToolbar` owns some special filter buttons; `SummaryCard` owns metric navigation/filter semantics. | Reuse existing owners. Don't convert a link into a button or remove a filter toggle's `aria-expanded`/Escape behavior. Review action placement at 420px/900px/1440px and keyboard focus before UI migration. |

## P4-A contract and preservation matrix

- `PanelHeader`: `title: string`, optional `count: number`, optional `actions: ReactNode`,
  optional `level: 2 | 3` (default 2). Actions get one `panel-header-actions`
  wrapper; the heading remains a semantic h2 or h3, not a fake heading.
- **Connections:** Create Connection still appears only for users with management capability;
  disabled while there are no adapters. No change to selection or Connection mutations.
- **Roles list:** Refresh and Create Role remain ordinary `ActionButton` instances with their
  previous loading/error guards; only the wrapper changes. List filtering/selection is untouched.
- **Role editor:** retains a nested h3, Cancel callback and busy state.
- **Grants:** Refresh retains busy/access checks; no change to grant operations or confirmations.
- **Overview:** Latest runs retains its native link to `#operations`, not a button.
- Responsive: shared header and action groups wrap at narrow widths using existing tokens,
  without adding nested scrollbars. The long-page/scroll-region rules remain unchanged.

## P4-B selection contract and preservation

- **Visual owner:** `src/web/src/components/selectedRecord.css`, imported once from `main.tsx`.
  Selected table rows, selected filtered Asset items, selected Asset tree rows, and current Connection buttons
  all reuse `--color-surface-navigation-active` plus one leading accent using existing tokens.
  Ordinary hover is not the same as selected; the keyboard focus ring is independent.
- **Semantic owner:** `data-selected` is a styling hook on native tables/lists, **not** an ARIA role.
  Native record-opening buttons may use `aria-current="true"` for the item currently shown in a detail pane;
  never put `aria-current` on a native table `row`. The Asset hierarchy is an actual tree:
  its `treeitem` keeps `aria-selected`. Page links use `aria-current="page"`.
  Tab controls use `aria-selected`, never `aria-current`.
- **Scope:** existing row click behavior, button toggle behavior, routing, identity, permission checks,
  keyboard focus and virtualization stay unchanged. `LogicPage.tsx` stays untouched while PR #442
  owns the focused Activity inspector.
- **Reference:** W3C [WAI-ARIA 1.2](https://www.w3.org/TR/wai-aria/#aria-current)
  and [WAI Technique ARIA26](https://www.w3.org/WAI/WCAG21/Techniques/aria/ARIA26) distinguish
  current navigation items from selected tabs/tree items.
- **Evidence boundary:** structural/interaction tests verify attribute behavior and one CSS owner.
  A rendered contrast, hover, focus and 320/420/900/1440px audit still requires working visual tooling;
  the advisory screenshot runner is tracked in #438.

## Validation checklist

- [x] Use a feature branch off the merged Phase 3 `develop` head and keep the scope narrow.
- [x] Keep source changes limited to shared header and low-conflict consumers; no backend, authorization,
  API contract or persistence changes.
- [x] Test optional count/action rendering, nested heading semantics, and callback activation.
- [x] Add a convention guard for migrated header surfaces and shared responsive action style; remove
  the legacy `role-actions` CSS exception.
- [ ] GitHub CI: frontend TypeScript, lint, unit/interaction tests, production build, integration and installer.
- [ ] Advisory before/after browser inspection of affected panels at 420px, 900px, 1440px when the
  runner is available (track separately in #438; a broken runner is not a regression).
- [x] P4-B selection implementation and regression guards in a separate Phase 4 PR; CI confirmation remains required.
- [ ] Remaining Phase 4 slices: P4-C/D after the form/notice component ownership is settled.

## Coordination and non-goals

- PR #442 is changing the responsive Logic Activity inspector. Do not touch its layout while it is active.
- PR #418 defines control/recommendation form and notice patterns; implement those **once**, with
  the existing owner, before migrating control screen markup.
- No design-system library adoption, no global component rewrite, no button-vs-link semantic changes,
  no new server endpoints and no change in access checks.
