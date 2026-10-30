# UI conventions

The rules the web app follows so every page looks and behaves the same. Each rule has a guard test in
`src/web/tests/` so a new page cannot quietly drift. Sources: Carbon, Atlassian, SAP Fiori, GitHub Primer and
Material guidance on dense data applications.

## Components (use these, never restyle a button or box per page)

| Need | Use |
|---|---|
| Any button | `ActionButton` (`primary` once per area, `secondary`, `quiet`; `iconOnly` for symbols) |
| Close a details pane | `CloseButton` (✕ icon, tooltip, accessible name) |
| Search | `SearchInput` (always with a placeholder) |
| Filters | `FilterBar` / `FilterField` (`trailing` slot for the refresh icon) |
| Copy | `CopyButton` |
| Status | `Status` |

**Icon or text?** Icon-only for universally understood actions (close, refresh, copy, expand/collapse, overflow), always
with a tooltip and accessible name. Text for primary and commit actions (Save, Approve, Configure Logic), anything with a
consequence, and anything whose icon is ambiguous.

## Layout

- Master-detail pages (`split-layout`): on screens wider than 900px the panes fill the window under the header and each
  scrolls on its own; the page does not scroll. `useSplitPaneViewport` measures the offset, `splitLayout.css` holds the one
  shared rule. On phones list and details replace each other.
- Beside an open item the list gets 2 parts and the details 3 (ratios, never fixed widths).
- Lists are the shared table: Status first, whole row opens the item, selected row highlighted, the name stays a button for
  keyboard and screen readers.
- Details are one bordered, full-height card with a single frame (Activity shares the list panel instead).
- Gutters: panel headings, filters and table text all start on `--space-18`.

## Type

- Three weights from tokens: regular 400, medium 500, bold 700 (Roboto ships only these; 600 would render as 700).
  Headings, controls, tabs, column headers and list names are medium; bold is for emphasis in a sentence and big figures.
- Sizes: 12px caption/small, 14px controls/body-small/tabs/navigation, 16px body, 20px metric, 24-32px headings.
- Line height 1.45 body, 1.25 headings.

## Units

`rem` for spacing, control, radius, layout and font sizes (value / 16); `px` only for hairlines (borders, focus ring),
spinner strokes, shadows, the pill radius and the measured `--pane-top`. Everything goes through `src/tokens.css`.

## Backlog

- Migrate the remaining raw `<button>` elements (about 40) to `ActionButton`, with a shrinking allowlist in the guard test.
- Move Runs, Assets and Logic filters with saved views onto `FilterBar`.
- Pin the detail header (title and actions) while the details body scrolls.
- Media-query breakpoints in `em` so layouts reflow with text size.
- Clickable stat cards on Recommendations; empty states with a next action; Activity day grouping.
- Readable names for remaining raw identifiers; consistent target-type casing; shareable event URLs.
- Grants selects are 38px, controls are 36px.
