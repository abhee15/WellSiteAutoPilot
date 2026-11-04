# UI conventions

The rules the web app follows so every page looks and behaves the same. Each rule has a guard test in
`src/web/tests/` so a new page cannot quietly drift. Sources: Carbon, Atlassian, SAP Fiori, GitHub Primer and
Material guidance on dense data applications.

## Components (use these, never restyle a button or box per page)

| Need | Use |
|---|---|
| Any button | `ActionButton` (`primary` once per area, `secondary`, `quiet`; `iconOnly` for symbols) |
| Panel heading, optional count and controls | `PanelHeader` (`title`, optional `count`, `actions`, and `level=3` only for nested editors); actions share one wrapping token-based layout |
| Close a details pane | `CloseButton` (✕ icon, tooltip, accessible name) |
| Search | `SearchInput` (always with a placeholder) |
| Filters | `FilterBar` / `FilterField` (`trailing` slot for the refresh icon) |
| Copy | `CopyButton` |
| Status | `Status` |

**Icon or text?** Icon-only for universally understood actions (close, refresh, copy, expand/collapse, overflow), always
with a tooltip and accessible name. Text for primary and commit actions (Save, Approve, Configure Logic), anything with a
consequence, and anything whose icon is ambiguous.

## Tab and page-section navigation (migration contract)

See [Tab navigation design-system inventory and contract — Phase 1](tab-navigation-design-system-phase1.md) for the source audit, proposed component APIs, behavioral preservation matrix, responsive decisions, and future CI guard requirements. **Phase 3 implementation:** `TabStrip`, `PageTabNav`, and `SectionTabs` are shared throughout Logic, Assets, Administration, and Configured Logic details. URL-addressable sections are links; local detail panels are manual-activation tabs. The old `DetailTabs` component and page-specific tab CSS have been removed. The global consistency guard rejects duplicate tab markup and legacy tab CSS.

- **Page-level views:** use the shared `PageTabNav` for URL-addressable sections (Assets, Logic, Administration). It renders labeled native links and `aria-current="page"`, **not** tablist/tab roles. Preserve hashes, counts, permissions and Add/Create actions (the latter remain ordinary `ActionButton` controls outside the navigation strip).
- **Within a record's detail pane:** use the shared `SectionTabs` for real content tabs and matching tabpanels. Roving keyboard focus must use Left/Right/Home/End; Enter/Space commits a manual-activation tab, especially when selecting it fetches remote history. Preserve draft-edit locks, controlled selection and access filtering.
- **Appearance:** both use one private `TabStrip` visual foundation, the existing light semantic tokens, shared selected/focus/hover styles and the 14px control type scale. Do not add page-specific tab CSS or a parallel tab library.
- **Responsiveness:** maintain the existing 2:3 master/detail layout. Put page navigation above the panes, inspector tabs inside the selected pane; size to available pane width and provide horizontally reachable overflow without wrapping labels. Add/Create actions must remain visible. Check desktop, tablet, phone, zoom, keyboard and touch.
- **Prevention:** after migration, CI must reject ad-hoc tablist markup or tab-strip CSS outside shared components and remove old exceptions. A CSS class merely having a rule or using design tokens is **not** sufficient evidence of component consistency.

See [Phase 4 component consistency audit](design-system-consistency-phase4.md) for the cross-screen inventory, ownership, first header migration and remaining form/notice/selection work. Form fields and notices follow the existing [Control UI audit](control-ui-audit.md) D1 ownership; do not build a competing kit.

## Layout

- Master-detail pages (`split-layout`): on screens wider than 900px the panes fill the window under the header and each
  scrolls on its own; the page does not scroll. `useSplitPaneViewport` measures the offset, `splitLayout.css` holds the one
  shared rule. On phones list and details replace each other.
- Beside an open item the list gets 2 parts and the details 3 (ratios, never fixed widths).
- Lists are the shared table: Status first, whole row opens the item, selected row highlighted, the name stays a button for
  keyboard and screen readers.
- Details are one bordered, full-height card with a single frame (Activity shares the list panel instead).
- Gutters: panel headings, filters and table text all start on `--space-18`.

## Selected records and keyboard focus

- Selected records in tables, list results and Asset trees, plus the current Connection, share one
  token-based visual appearance from `components/selectedRecord.css`. Never create per-page
  selected-row backgrounds or indicators; the selected surface is not merely a hover color.
- A detail-opening **button** may mark the currently shown record with `aria-current="true"`
  while keeping its existing `aria-expanded` and `aria-controls` behavior. Native table rows
  use `data-selected` for appearance, **not** invented `role="grid"` or `role="tab"`.
- Actual Asset tree items use `aria-selected`; true tabs use `aria-selected`; navigation links use
  `aria-current="page"`. Maintain the meaning of each rather than adopting one attribute everywhere.
- Focus indication comes from `:focus-visible` and must remain independently visible
  regardless of selected/hover state. Review keyboard and responsive behavior separately.
- See [Phase 4 design audit](design-system-consistency-phase4.md) for the cross-screen scope and
  [WAI-ARIA](https://www.w3.org/TR/wai-aria/#aria-current) for the current/selected distinction.

## Type

- Three weights from tokens: regular 400, medium 500, bold 700 (Roboto ships only these; 600 would render as 700).
  Headings, controls, tabs, column headers and list names are medium; bold is for emphasis in a sentence and big figures.
- Sizes: 12px caption/small, 14px controls/body-small/tabs/navigation, 16px body, 20px metric, 24-32px headings.
- Line height 1.45 body, 1.25 headings.

## Units

`rem` for spacing, control, radius, layout and font sizes (value / 16); `px` only for hairlines (borders, focus ring),
spinner strokes, shadows, the pill radius and the measured `--pane-top`. Everything goes through `src/tokens.css`.

## Scrolling

On tablets and wider (>= 48em) the window never scrolls. The navigation and page header stay put and the page body is
the one scroll region; master-detail pages give each pane its own scroll instead. Never put a scroller inside another
scroller (nested scroll areas are hard to use with keyboard, screen readers and magnifiers). Phones scroll the page.

## Search and filters

Every list uses `FilterToolbar`: one search box and a Filters button (with the active count) on a row, the fields in a panel
that opens in place, a chip per applied filter, Clear filters and a result count. Saved views live in a collapsed section of
the panel. The toolbar adapts to its own width (container query), so a narrow list pane stacks like a phone.

Asset hierarchy controls opt into `FilterToolbar`'s `sticky` behavior above the split-pane breakpoint. The compact search/filter toolbar stays at the top of its own scroll pane. Opening Filters returns the toolbar to normal flow and resets only that pane to the top, keeping tall filter and saved-view forms reachable without nested scroll regions. Escape closes the panel and returns keyboard focus to Filters. On smaller screens controls stay in normal page flow; the existing container-responsive layout remains in use.

## Breakpoints

In `em`, range syntax: `(width <= 40em)` phone, `(width < 48em)` / `(width >= 48em)` tablet, `(width <= 56.25em)` /
`(width > 56.25em)` split panes, `(width <= 68.75em)` wide forms. A guard test rejects px breakpoints.

## Definition of done for a screen

A screen is reviewed against the list in `control-ui-audit.md` section 5: specified first, shared components only, every
class has a style rule (`css-coverage.test.mjs`), tokens not values, text through `t()`, works at every size and by
keyboard, seen rendered, and guarded by a test. New screens do not merge until each point is answered in the pull request.

## Backlog

- Pin the detail header (title and actions) while the details body scrolls. It needs the detail panels restructured so the header sits outside the scrolling body; a sticky header inside the current wrappers ends early.
- Audit events could name the user and role of a grant; the server records only their ids, so the server would need to add names to the event details first.
