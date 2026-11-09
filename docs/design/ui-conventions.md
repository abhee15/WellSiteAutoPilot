# UI conventions

The rules the web app follows so every page looks and behaves the same. Each rule has a guard test in
`src/web/tests/` so a new page cannot quietly drift. Sources: Carbon, Atlassian, SAP Fiori, GitHub Primer and
Material guidance on dense data applications.

## Components (use these, never restyle a button or box per page)

| Need | Use |
|---|---|
| Any button | `ActionButton` (`primary` once per area, `secondary`, `quiet`; `iconOnly` for symbols) |
| Table | `DataTable` (`label` is required and becomes the accessible name; `variant="embedded"` for a small fact table inside running content). It owns the scroll wrapper; pages add layout hooks through `className` only |
| Standalone contextual destination in a row/card | `ContextLink`; use `appearance="record"` for record names/timestamps in tables and fact lists. Inline prose links stay native and underlined; workflow entry beside buttons uses `ActionLink` |
| Initial panel content is unavailable | `PanelLoading`; center within the affected panel, keep navigation usable, retain populated content on background refresh |
| Navigation presented alongside action buttons | `ActionLink` from `ActionButton.tsx`; same shared action styles and sizing, native anchor semantics. Never use the compact `.action-link` row style for toolbar actions. |
| Panel heading, optional count and controls | `PanelHeader` (`title`, optional `count`, `actions`, and `level=3` only for nested editors); actions share one wrapping token-based layout |
| Close a details pane | `CloseButton` (✕ icon, tooltip, accessible name) |
| Search | `SearchInput` (always with a placeholder) |
| Filters | `FilterBar` / `FilterField` (`trailing` slot for the refresh icon) |
| Copy | `CopyButton` |
| Status | `Status` |
| Native labelled fields | `TextField`, `NumberField`, `TextAreaField`, `SelectField` from `FormFields`; helper/error IDs, required, disabled and read-only state belong to the component |
| Choices | `CheckboxField` and `RadioGroup`; native keyboard behavior, visible labels and a fieldset/legend for the group |
| Titled part of a form or detail view, collapsible or not | `Section` and `CollapsibleSection` (`components/Section.tsx`). One heading size, one divider, one chevron marker; a collapsible one is a native `<details>`, so the browser owns keyboard and find-in-page. `quiet` is for technical detail most people never open. Never put a bare `<details>` or a raw `<h4>` step in a form |
| Form actions / existing consequential confirmation | `FormActions` and non-modal `ConfirmPanel`. One row, Cancel or Discard first and the primary action last (guarded). A destructive action goes in `FormActions leading`, apart from the primary one. A yes/no confirmation is also Cancel (No) then the action (Yes) |
| Context / progress / outcomes | `Notice`; static guidance uses `announce={false}`, runtime error uses error tone/alert, progress/success uses status |
| Summary metric / filter | `SummaryCard`: linked cards have a chevron; filter cards have a filter/check cue; optional semantic tone is reserved for meaningful nonzero attention counts |

**Icon or text?** Icon-only for universally understood actions (close, refresh, copy, expand/collapse, overflow), always
with a tooltip and accessible name. Text for primary and commit actions (Save, Approve, Configure Logic), anything with a
consequence, and anything whose icon is ambiguous.

**Actions versus record names.** Retry, Show more, Remove and Select wells are actions: `ActionButton` (`quiet` for a
low-emphasis action repeated inside a row or notice, `secondary` for a standalone one). A record's name that selects it in a
master-detail list is the one raw button, `asset-select` or `row-link`, because `ContextLink` is only for hash destinations.
`tests/consistency.test.mjs` pins how many of those each file may hold; the count can only shrink. A shared record-name button
would retire the list.

## Tab and page-section navigation (migration contract)

See [Tab navigation design-system inventory and contract — Phase 1](tab-navigation-design-system-phase1.md) for the source audit, proposed component APIs, behavioral preservation matrix, responsive decisions, and future CI guard requirements. **Phase 3 implementation:** `TabStrip`, `PageTabNav`, and `SectionTabs` are shared throughout Logic, Assets, Administration, and Configured Logic details. URL-addressable sections are links; local detail panels are manual-activation tabs. The old `DetailTabs` component and page-specific tab CSS have been removed. The global consistency guard rejects duplicate tab markup and legacy tab CSS.

- **Page-level views:** use the shared `PageTabNav` for URL-addressable sections (Assets, Logic, Administration). It renders labeled native links and `aria-current="page"`, **not** tablist/tab roles. Preserve hashes, counts, permissions and Add/Create actions (the latter remain ordinary `ActionButton` controls outside the navigation strip).
- **Within a record's detail pane:** use the shared `SectionTabs` for real content tabs and matching tabpanels. Roving keyboard focus must use Left/Right/Home/End; Enter/Space commits a manual-activation tab, especially when selecting it fetches remote history. Preserve draft-edit locks, controlled selection and access filtering.
- **Appearance:** both use one private `TabStrip` visual foundation, the existing light semantic tokens, shared selected/focus/hover styles and the 14px control type scale. Do not add page-specific tab CSS or a parallel tab library.
- **Responsiveness:** maintain the existing 2:3 master/detail layout. Put page navigation above the panes, inspector tabs inside the selected pane; size to available pane width and provide horizontally reachable overflow without wrapping labels. Add/Create actions must remain visible. Check desktop, tablet, phone, zoom, keyboard and touch.
- **Prevention:** after migration, CI must reject ad-hoc tablist markup or tab-strip CSS outside shared components and remove old exceptions. A CSS class merely having a rule or using design tokens is **not** sufficient evidence of component consistency.

See [Phase 4 component consistency audit](design-system-consistency-phase4.md) for the cross-screen inventory, ownership, first header migration and remaining form/notice/selection work. Form fields and notices follow the existing [Control UI audit](control-ui-audit.md) D1 ownership; do not build a competing kit.

## Layout

Panel headers, edge table cells, filters and content states share `--space-panel-inline`. Do not override shared action, tab, link or loading appearance in a page stylesheet. See [panel loading, link, border and alignment contracts](panel-loading-link-border-contracts.md) for behavior, border roles, guards and remaining migration scope.

- Master-detail pages (`split-layout`): on screens wider than 900px the panes fill the window under the header and each
  scrolls on its own; the page does not scroll. `useSplitPaneViewport` measures the offset, `splitLayout.css` holds the one
  shared rule. On phones list and details replace each other.
- Beside an open item the list gets 2 parts and the details 3 (ratios, never fixed widths).
- Lists are the shared table: Status first, whole row opens the item, selected row highlighted, the name stays a button for
  keyboard and screen readers.
- Details are one bordered, full-height card with a single frame (Activity shares the list panel instead).
- Gutters: panel headings, filters and table text all start on `--space-18`.

## Stylesheets

One class has one owner file: its base rules, state tones and responsive variants live together, and a page stylesheet may
add layout hooks but not restate a shared class. `!important` is not used. The one exception is the `.empty-state` padding and
alignment, which must beat every table layout (including the narrow card layouts) without depending on stylesheet order;
`tests/css-tokens.test.mjs` pins it. The three narrow-screen table layouts (activity feed, operations, recommendations) differ
in structure on purpose and stay with their pages.

## Type

- Three weights from tokens: regular 400, medium 500, bold 700 (Roboto ships only these; 600 would render as 700).
  Headings, controls, tabs, column headers and list names are medium; bold is for emphasis in a sentence and big figures.
- Sizes: 12px caption/small, 14px controls/body-small/tabs/navigation, 16px body, 20px metric, 24-32px headings.
- Line height 1.45 body, 1.25 headings.

## Detail copy

Lead with record identity, current state, values and available actions. Each sentence must help the user
identify the record, understand a meaningful exception or complete the current task.

- Include the zone in each formatted timestamp; do not add a paragraph explaining local-time display.
- Omit routine schedule-agreement reassurance and revision implementation notes. When the live schedule
  actually differs, show the saved frequency once beside the effective state and existing controls.
- Do not repeat a successful operational-read timestamp in a footer. Preserve refresh failure, stale/revision-change
  and unavailable messages, retry actions, field requirements and consequential-action acknowledgements.
- Keep the shared-view schedule scope: changing one view assignment affects its members, which matters to the task.
- Avoid moving essential instructions into tooltips. Technical provenance can use the existing disclosure.

This is a content decision, not a blanket rule to delete helper text. The retired boilerplate is guarded in
`tests/consistency.test.mjs`. Follow-up reviews must exercise failed, read-only and changed states as well as normal data.
Guidance: [Atlassian content](https://atlassian.design/foundations/content/),
[W3C cognitive accessibility: avoid too much content](https://www.w3.org/WAI/WCAG2/supplemental/patterns/o5p03-manageable-quantity/).

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

## Words, dates and time zones

Wording and formatting have one owner each, so the same value reads the same on every screen:

- **Table column headers** are message keys, `<th>{t('page.colName')}</th>`, never literal text. Keys are scoped to the page
  that owns the table (`operations.colStatus`), not borrowed from a form label that happens to read the same.
- **A status shown as text** goes through `statusLabel` in `words.ts` (`NotReturned` reads "Not returned"). A status badge
  is the `Status` component. The raw server value is never printed; keep it in a tooltip or disclosure if support needs it.
- **Dates and times** are formatted by `formatTimestamp` in `format.ts`, which always carries the time zone. Screens do not
  call `toLocaleDateString`, `toLocaleTimeString` or `toLocaleString` themselves.
- **Time zone choices** come from `timeZoneChoices()` in `views/assignmentSchedule.ts`: the saved zone first, the browser's
  zone, UTC, then every zone the browser lists. No screen names a zone.

Guards in `tests/consistency.test.mjs` fail on a literal `<th>`, a raw `{x.status}`, a direct `toLocale*String` call outside
the formatters, and a named time zone; `tests/schedule-time-zones.test.mjs` covers the zone list.

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

See [visual hierarchy and task-flow review](visual-flow-review.md) for page order, compact row context, border checks and evidence limits. Summary card appearance belongs to `components/summaryCard.css`; pages may position cards but not override their visual rules.

Shared forms and notices migrate under [#467](https://github.com/abhee15/WellSiteAutoPilot/issues/467); see [component contract and first recommendation migration](shared-form-notices.md). Recommendation decision and explicit execution are migrated first. The explicit legacy field/notice inventory in tests can only shrink; a migrated screen cannot reintroduce raw controls or notices. Specialized search/filter/multi-select components keep their existing ownership.

Where and how a loading indicator appears follows one contract: [loading states](loading-states.md). Initial
content of a panel, pane or detail uses `PanelLoading`; a read inside a section is small and left-aligned in the
section; append progress is small and inline. The guard rejects a default-size block spinner in any view. See also
[initial panel loading adoption](panel-loading-adoption.md) for the reproduced gutter mismatch and rendered
delayed-read/terminal-state checks.
