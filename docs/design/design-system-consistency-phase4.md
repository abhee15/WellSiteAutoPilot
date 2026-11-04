# Phase 4 — Cross-screen design-system consistency audit

Status: **Phase 4 inventory and P4-A implementation.** Audited against `develop` on 2026-11-04.
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
| **P4-B, selection treatment** | `OperationsPage`, `LogicPage`, `RecommendationsPage`, `ActivityPage`, and `RoleCatalogPage` mark selected table rows with `data-selected`; `splitLayout.css` owns the common highlight for these runs-layout tables. `ConnectionsPage` correctly marks its selectable Connection buttons with `aria-current`. Asset tree uses `aria-selected`. | First verify equivalent selected styling, focus order and contrast through a browser/a11y review. If a gap exists, refactor the *shared* table/list style; retain semantic distinctions and permission-driven row affordances. Avoid reworking the active Logic Activity inspector PR #442. |
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
- [ ] Additional Phase 4 slices: P4-B selection review and P4-C/D after component ownership is settled.

## Coordination and non-goals

- PR #442 is changing the responsive Logic Activity inspector. Do not touch its layout while it is active.
- PR #418 defines control/recommendation form and notice patterns; implement those **once**, with
  the existing owner, before migrating control screen markup.
- No design-system library adoption, no global component rewrite, no button-vs-link semantic changes,
  no new server endpoints and no change in access checks.
