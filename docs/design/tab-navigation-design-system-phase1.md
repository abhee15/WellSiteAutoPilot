# Tab navigation design-system inventory and contract — Phase 1

Status: **Phase 1 design contract; no production UI changes in this PR.**
Baseline: `develop` as inspected on 2026-11-03.
Next steps: Phase 2 shared primitives and initial migration; Phase 3 remaining migration and enforceable guards.
Related: [UI conventions](ui-conventions.md), [Control UI audit](control-ui-audit.md), [Logic details refinement](logic-details-refinement.md), [Frontend tokens](../development/design-tokens.md).

## 1. Problem and scope

The web application has **four tabbed navigation surfaces implemented through three separate styling mechanisms**. All use semantic design tokens but do not share a component contract. Spacing, active colors, hover behavior and keyboard interactions diverge. Existing checks enforce token validity and the presence of CSS rules, not that a page uses an approved tab component.

This is a **component-ownership and prevention problem**, not a request to redraw one Logic screen. This phase records evidence, behavior-preservation requirements, a proposed architecture, accessibility and responsive contracts, test gates, and the order of implementation. It deliberately does not ship new components, change CSS or modify routing.

In scope: Assets, Logic, Configured Logic detail tabs, and Administration tabs; shared styling/semantics; documented verification and regression requirements.
Out of scope: changing feature IA, moving content between panes, changing permissions or server behavior, unrelated form/notice restyling, introducing a new UI framework, and the broader Control UI rework tracked in [the existing audit](control-ui-audit.md).

## 2. Source inventory

| Surface | Source and current styling | Navigation/state and important behavior | Evidence and gaps |
| --- | --- | --- | --- |
| **Logic (page-level)**: Configured logic / Schedules / Modules | `src/web/src/views/LogicPage.tsx`, `views/splitLayout.css`, `styles.css`; `.split-tabs` | Links `#logic`, `#logic/schedules`, `#logic/modules`; permission-filtered items, counts, trailing Create action. The selected configuration is addressed separately in the same hash scheme. | `role=tablist` with `<a role=tab>`, but there are no matching `tabpanel` IDs/`aria-controls` and no roving tab focus. Link navigation should use native link semantics. |
| **Assets (page-level)**: Assets / Asset types | `src/web/src/views/AssetsPage.tsx`, `views/splitLayout.css`; `.split-tabs` | Links `#assets` and `#assets/types`; counts and trailing Add action; assets and type layouts differ; selection and list/detail navigation use hash-backed state. | Same page-link-as-tab semantic mismatch as Logic. |
| **Administration (page-level)**: Users / Roles / Grants / Activity | `src/web/src/views/AdministrationWorkspace.tsx` and `styles.css`; `.administration-tabs` with `.secondary-button` | Hash `#access` / `#access/roles` / `#access/grants` / `#access/activity`; nested event links via `#access/activity/<event-id>`; selecting a user to assign a role temporarily carries user data into Grants. | Third independent CSS implementation; buttons use tab ARIA but lack arrow/Home/End handling and roving tab stops. A direct click on Grants intentionally clears the previously assigned user; this behavior must survive migration. |
| **Configured Logic detail (inside right pane)**: Overview / Configuration / Activity | `src/web/src/views/ConfiguredLogicWorkspace.tsx`, `components/DetailTabs.tsx`, `components/detailTabs.css`; `.detail-tabs` | Controlled state, stable generated IDs and tabpanels, conditional Activity permission, Activity history fetched lazily, switching disabled while editing/busy, changing tabs clears pending confirmation. | True in-place tabpanel pattern. Current Arrow/Home/End code *also selects*, which can launch lazy work on focus movement; keyboard navigation must be specified with loading behavior in mind. |

The asset tree's `aria-selected` is a tree-selection state, **not** another tab implementation. Likewise filtering buttons, accordions and master/detail row selection are not tabs; do not migrate them by string-matching the word “selected.”

### Measured differences

| Property | `split-tabs` | `detail-tabs` | `administration-tabs` |
| --- | --- | --- | --- |
| Container | Flex, wraps, 4-space gap, bottom border | Flex, wraps, 4-space gap, bottom border | Flex, wraps, 4-space gap, bottom border and bottom margin |
| Tab horizontal padding | `--space-16` | `--space-12` | `--space-16` |
| Selected text | `--color-text-primary` | `--color-text-link` | `--color-text-primary` |
| Selected indicator | `--color-focus-ring` | `--color-text-link` | `--color-focus-ring` |
| Hover | No tab-specific rule | No tab-specific rule | Custom button hover background |
| Mobile | Wraps; no dedicated behavior | Wraps; no dedicated behavior | Wraps; buttons use `flex: 1` at `width <= 40em` |
| Interaction | Native links, incorrectly assigned tab roles | Keyboard-operable buttons, immediate activation | Buttons with tab roles, click-only activation |

Shared appearance exists only by coincidence in selectors scattered across three stylesheets.

### Why existing guards missed it

- `src/web/tests/style-tokens.test.mjs` validates tokens and colors, not component identity or visual equivalence.
- `src/web/tests/css-coverage.test.mjs` verifies that class names are styled; it does not disallow duplicate, validly styled tab components. `detail-tab` is currently explicitly listed in its legacy exception baseline even though descendant selectors style its button.
- `src/web/tests/consistency.test.mjs` has conventions for buttons, search, tables and layout, but no ban on new tablists outside a shared component. It explicitly allows `.administration-tabs button` as an exception to page-specific button styling.
- Interaction tests verify behavior for individual pages. For example, `tests/interaction/logic-details-workspace.test.tsx` expects an ArrowRight press to immediately change selection; `tests/interaction/deep-links.test.tsx` expects Administration tabs to have `role=tab`. Both will need contract-aligned updates rather than silently breaking.

**Finding:** tokens and conventions exist; the mandatory tab component and enforced semantics do not.

## 3. Proposed component contract

Create a **single visual tab-strip foundation**, shared by two behavior-specific public components. Do not make a “universal” component with contradictory ARIA semantics.

### 3.1 `PageTabNav` — navigation to addressable views

Use for Assets, Logic and Administration, even when those destinations are visually tab-like.

- Takes a localized accessible navigation label, an ordered set of items (`id`, localized `label`, `href`, optional `count` and permission-filtered visibility), `activeId`, and an optional **separate trailing action slot**.
- Renders a labeled `<nav>` containing real `<a href>` links. Mark the active link with `aria-current="page"`. Do **not** add `role=tab`, `role=tablist`, `aria-selected` or a synthetic `tabpanel` association to page links.
- Preserve bookmarkable hash URLs, browser Back/Forward behavior, native link activation, modifier keys, and focus visibility. No arrow-key roving focus is required for ordinary navigation links; Tab should reach each link.
- Permission-based omissions are decided by the consuming page as today, not by CSS hiding inaccessible destinations.
- The trailing Create/Add control remains an ordinary `ActionButton`, outside the scrolling link strip and outside its accessible navigation items; preserve permission, disabled and open-editor behavior.
- Administration's “Assign role → Grants” path must preserve the preselected user; a user deliberately selecting Grants directly must still clear that handoff state. Avoid an unguarded migration to links that loses this behavior.

### 3.2 `SectionTabs` — switch tabpanels without changing page

Use for Configured Logic details, and for future genuine intra-panel tabs.

- Takes localized `label`, typed tab IDs/labels, controlled `selected` and `onSelect`, stable prefix/IDs and a supported `disabled` state. Keep state owned by the feature (including permission gating and edit locks).
- Renders `role=tablist` containing `role=tab` buttons with `aria-selected`, `aria-controls`, one page-tab-stop via roving `tabIndex`, and associated `role=tabpanel` regions with `aria-labelledby`. Keep inactive panels hidden semantically and visually; ensure selected panel content is reachable by keyboard when no naturally focusable child starts the panel.
- Use **manual activation** for panels with lazy, remote or expensive content: Left/Right/Home/End move focus only; Enter/Space or click commits `onSelect`. This avoids starting Activity loads when merely exploring tabs. An automatically activating variant may be considered only for instant, fully present panels, with explicit evidence and tests—not a different CSS implementation.
- Arrow movement skips unavailable/disabled tabs. If switching is blocked by a draft/edit/busy lock, focus and selection remain stable; do not discard typed input. Permission loss from a now-hidden selected tab must move selection to the authorized fallback (currently Overview).
- Preserve `useId`-based ID uniqueness across multiple detail panes, Activity lazy initialization, revision state, and the current clearing of pending activation confirmations on committed tab changes.

### 3.3 `TabStrip` — private visual foundation

Both public components consume one visual primitive and stylesheet under `src/web/src/components/`. This layer owns appearance only: typography, spacing, selected indicator, focus ring, hover, disabled treatment, count styling, overflow and alignment. It does not select content, change hashes, inspect permissions or own revision-editor state.

Proposed visual baseline for implementation review (validate against rendered before/after screenshots rather than treating markup alone as proof):

- Follow existing neutral, light UI and tokens; **no pill buttons, raised cards or new palette**.
- Tab text `--font-size-body-small` (14px), regular until selected, medium when selected. Nonselected text uses `--color-text-subtle`; selected text uses `--color-text-primary`. Count text remains subordinate.
- One underline indicator using `--border-width-thick` and the existing accent/focus semantic token; no second independent indicator color. Separate visible keyboard focus outline using shared `--focus-ring-width`, `--focus-ring-offset` and `--color-focus-ring`. Do not let the selected underline substitute for keyboard focus.
- Spacing and target dimensions use existing `--space-*` and `--size-*` tokens. Start from page-tab `--space-10` vertical / `--space-16` horizontal padding, then visually check density inside the detail pane. Prefer one specification over arbitrary “small/large” variants.
- Preserve the existing 2:3 master/detail pane ratio and `--space-18` panel gutters. Page navigation stays above the panes; SectionTabs stays **inside the right inspector**, aligned with its content. Do not introduce a second framed card or nested vertical scroller.
- Fit naturally when labels fit; when they do not, make the tab *strip* horizontally scrollable without wrapping into ambiguous multiple rows. Its visible active/focused item must remain reachable/visible (including keyboard and magnification). The optional trailing page action must not disappear into the scrolling strip. Verify at the pane width, not just a viewport breakpoint.
- At touch/coarse-pointer widths satisfy `--size-touch-target` (44px) minimum hit area. Honor zoom, reduced motion and high-contrast/focus visibility; do not use color as the only selection cue.

A dedicated tab component may use buttons **inside the shared component**; the existing general rule “use ActionButton for actions” must gain this narrow documented exception rather than making all tabs look like action buttons.

## 4. Preserve these behaviors during migration

| Scenario | Required result | Existing regression anchor |
| --- | --- | --- |
| Open `#logic`, `#logic/modules` or `#logic/schedules` directly; navigate Back/Forward | Correct authorized page and selected nav item, unchanged counts and trailing create control | `logic-summary.test.tsx` and page navigation tests |
| Open `#assets` and `#assets/types`; select asset or type | Existing hierarchy, type views, counts, and Add flow survive | `asset-page.test.tsx`, `deep-links.test.tsx` |
| Open `#access/roles` and `#access/activity/<id>` | Correct Administration view / selected audit item, with link semantics; User → Assign role carries target into Grants; direct Grants clears it | `deep-links.test.tsx` plus new handoff regression |
| Select Configured Logic detail; navigate with keyboard | Manual focus movement and Enter/Space activation, correct ARIA; Activity loads only on activation | `logic-details-workspace.test.tsx` |
| Edit a draft, save/cancel, lose permissions, or get live refresh | No accidental tab switch, lost input, stale protected detail or remount on background update | `logic-details-workspace.test.tsx`, `editable-logic-drafts.test.tsx` and authorization tests |
| Desktop with both panes, <=56.25em single-pane transition, <=40em phone, zoom/narrow inspector | One legible strip per level, no clipped action or duplicate page scroll; focus reachable | Split-layout interaction tests and required render comparisons |

Note: Some current tests target incorrect semantics (e.g. querying page navigation by role `tab`). Phase 2/3 must **update assertions to the new contract while also testing actual user outcomes**, not simply delete tests.

## 5. Enforceability and implementation gates

Phase 2/3 must extend `src/web/tests/consistency.test.mjs` (or introduce an equivalently enforceable targeted test) to:

1. Disallow `role="tablist"` and `role="tab"` and direct tab-strip CSS outside approved shared components. Exclude legitimate `role=tabpanel` usage only where required by the shared `SectionTabs` contract.
2. Disallow `.split-tabs`, `.administration-tabs` and independent tab styling once all consumers migrate; retire related `styles.css` and `splitLayout.css` selectors.
3. Remove `.administration-tabs button` from allowed page-level button exceptions and remove stale tab-related entries from `css-coverage.test.mjs`.
4. Test the guard **negatively** with intentional invalid fixture patterns; ensure normal tree/grid selected states and routing links do not falsely fail.
5. Add shared-component interaction tests: selection, keyboard movement/manual activation, disabled/permission cases, ARIA relations, deep links and browser history, focus retention after selection and when narrowing the pane.
6. Capture **before/after rendered views** for each of the four surfaces at wide/two-pane, tablet/narrow-panel and phone sizes. Check selected, hover, focus, disabled and overflow states; automated tests cannot prove pixel-level consistency alone.

Continue running the repo's existing `npm run test:ui`, `npm run test:interaction`, `npm run lint`, `npm run typecheck` and `npm run build` where generated client prerequisites are available. Follow the PR template, merge only with required checks green; do not alter unrelated authorization, API or database contracts.

## 6. Sequence and ownership

- **Phase 1 — this PR:** source inventory, architecture/behavior contract, preservation matrix and updated design-system conventions. Documentation only; no user-visible change.
- **Phase 2 — foundation + first consumers:** implement and test `TabStrip` / `PageTabNav` / `SectionTabs`; migrate **Logic and Assets**; keep existing navigation URLs and create/add actions. Adjust only tests affected by those two migrations.
- **Phase 3 — remaining consumers and prevention:** migrate **Administration and Configured Logic detail**; address manual activation and handoff behavior; remove legacy CSS and test exceptions; enforce no duplicate tabs and add rendered review evidence.
- **Phase 4 — separate consistency backlog:** inventory other repeated components (forms, notices, headings, status, selection state and action placement), compare with the existing Control UI rework, prioritize gaps and do not implement competing form/notice components. This is not a prerequisite for the tab fix.

### Definition of done for Phase 1

- All known tab surfaces and their divergent CSS/interaction rules are listed with source paths.
- Correct page-navigation vs in-panel-tab semantics are explicit.
- Shared visual ownership and no-duplicate architecture are defined.
- Responsive, keyboard, permission, editor/URL state and test regressions are enumerated.
- `ui-conventions.md` links to this contract; Phase 2 can implement without redesigning the behavior on each page.

## 7. External references

- [WAI-ARIA APG — Tabs pattern](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/) — role/label relationships, keyboard operation, when automatic activation is appropriate.
- [WAI-ARIA APG — Manual activation example](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/examples/tabs-manual/) — focus navigation independent of content activation.
- [IBM Carbon — Tabs guidelines](https://www.carbondesignsystem.com/building-blocks/core/components/tabs/guidelines) — enterprise tab hierarchy, visual states and responsive overflow; apply concepts through our tokens and components, not Carbon CSS or a second component framework.

The design choices above are repo-specific decisions for WellSite AutoPilot, not a requirement to adopt Carbon or another library.
