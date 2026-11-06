# Design-system consistency: tabs, ownership and enforcement

Status: **implementation plan only** (2026-11-03). Baseline: `develop` at `49d5b367083753ac0937e57a8ac1ab37e678f242`. No product behavior changes in this documentation PR.

## Why this work exists

The Logic page has page-level navigation (Configured logic / Schedules / Modules) and a second navigation level within a selected configuration (Overview / Configuration / Activity). The corresponding controls differ in padding, selected text color, CSS ownership, and keyboard behavior. The same problem exists elsewhere: Assets reuses the page-level style, while Administration has its own independently styled tab buttons.

This is an architectural gap, not just a color mismatch. The design system already defines palette, spacing, typography, and responsive tokens, but does not own an application-wide tab component contract. Functional tests cover individual flows, but no guard prevents a fifth independent tab implementation.

## Confirmed evidence from source

| Surface | Files and current implementation | Observed risk |
| --- | --- | --- |
| Logic page | `views/LogicPage.tsx` + `views/splitLayout.css`: `.split-tabs`, anchor elements marked `role="tab"` | Custom page-level tab styling; navigation-style links represented as local tab widgets |
| Assets page | `views/AssetsPage.tsx`: `.split-tabs` and anchor elements with tab roles | Same dependency as Logic; changing a local CSS rule can affect both |
| Configured Logic detail | `views/ConfiguredLogicWorkspace.tsx`, `components/DetailTabs.tsx`, `components/detailTabs.css` | Separate padding/selected colors; auto-selection with arrow keys; editor disables tab changes |
| Administration | `views/AdministrationWorkspace.tsx` + `styles.css`: `.administration-tabs`, `secondary-button` | A third style and tab behavior implementation; page-level URL state lives in `#access/...` |

Structural gaps:
- `tests/consistency.test.mjs` explicitly allows `.administration-tabs button` in its page-button styling exceptions.
- `tests/css-coverage.test.mjs` has a `detail-tab` entry in its exception baseline and checks for the existence of styling, **not** consistency of component ownership.
- `docs/design/ui-conventions.md` catalogs buttons/search/filter/status components but not canonical tab/navigation components.
- `docs/design/control-ui-audit.md` identifies similar unowned components (form fields/notices) with their own D1-D6 workstream and PR #418; avoid duplicating that scope.

This inventory is a code inspection, **not** a visual regression capture. Before implementation, capture representative current screens in the project's `tools/design-preview` environment.

## Proposed design-system contract

**One visual language, different semantics for different jobs.** Do not force both navigation behaviors through a single accessibility role.

1. **`PageTabNav`** (name may be refined during implementation): shared display for links that change URL-addressable top-level subareas, used by Logic, Assets and Administration. Render a labeled `<nav>` and normal anchors with `aria-current="page"` for the active destination, not `role="tab"`. Preserve existing hash routes and browser back/forward. Permit a trailing action slot for contextual actions such as Create, without making the action itself a tab.
2. **`DetailTabs`**: retain/refine the existing reusable in-page tab widget for selected-record content, with `role="tablist"`, `role="tab"`, `role="tabpanel"`, ids, `aria-selected`, `aria-controls`, roving tabindex, arrow/Home/End navigation and an accessible disabled state. Preserve current editor input-protection behavior and existing keyboard activation policy unless a separate measured change is agreed.
3. **Shared tab appearance**: put the common type scale, padding, indicator, foreground, focus, hover and disabled declarations in a single component-owned stylesheet built only on current semantic tokens. Any differences by navigation *level* must be explicit documented variants, never page-specific classes. Avoid introducing another theme or independent tab palette.
4. **Responsive behavior**: test the actual narrow browse/detail panels, desktop, tablet, and mobile. Select the final wrapping/overflow rule from rendered comparisons; do not assume the previously suggested overflow pattern is approved. Tab labels, focus indicators, and trailing actions must stay usable without introducing an additional page scroller or breaking the existing list-to-detail swap.

### Design decisions required before code migration

- Capture before screenshots at desktop, split-pane narrow width, tablet, and phone for Logic (list and selected detail), Assets, and Administration, including selected/focused/disabled states.
- Compare documented UI conventions, selected-state contrast and visual hierarchy. Decide the canonical padding/indicator/color and responsive overflow rule from those captures.
- Confirm page links should preserve existing hash routes and that details remain true ARIA tabs; record expected keyboard behavior in the design spec.

## Implementation sequence (small PRs against `develop`)

### DS-1 — Canonical tab foundation and tests
- Add `PageTabNav`, reuse/refine `DetailTabs`, and one component-owned tab stylesheet; retain stable prop contracts (items, selected, badges/counts, optional trailing actions, disabled only where appropriate).
- Add component-level tests for selected states, link targets, dynamic items, keyboard navigation, disabled behavior, and ARIA references.
- Document the approved tab specification in `docs/design/ui-conventions.md`; avoid unrelated global CSS rewrites.
- Verify the component in an isolated rendered preview at the widths above.
- Exit: one approved visual/semantic contract, a testable reusable implementation, no existing page behavior changed.

### DS-2 — Migrate Logic and Assets page navigation
- Replace `.split-tabs` page markup in `LogicPage.tsx` and `AssetsPage.tsx` with `PageTabNav`.
- Preserve Logic configured/schedules/modules routes, permission-dependent tab visibility, tab counts, Create action gating, and selection/schedule behavior.
- Preserve Assets/Types routing, counts, Create action, selected record / pane changes, and hash-based deep links.
- Remove obsolete page-level tab styling only when both users have migrated.
- Exit: same links and actions work, same browsing state preserved, screenshots show shared styles in both pages.

### DS-3 — Migrate Administration and align Configured Logic detail
- Replace `administration-tabs` and its `secondary-button` styling with `PageTabNav`, preserving Users/Roles/Grants/Activity routes and event-specific deep links, `Assign role` handoff, and permissions.
- Have Configured Logic details consume the shared appearance via `DetailTabs` without changing revision/edit semantics, unsaved user input, current tab selection, or activity query gating.
- Remove obsolete `.administration-tabs` / independent `.detail-tabs` style variants; preserve the existing `DetailTabs` component import where possible to minimize churn.
- Exit: all four surfaces share approved appearance; links vs local tabs have correct accessible semantics.

### DS-4 — Prevention and verification
- Add a focused architecture guard in `tests/consistency.test.mjs` (or an ESLint/AST rule if pattern matching would be fragile): no page-owned `role="tablist"`/`role="tab"` markup, no copied `aria-selected` tab widgets, no new page-specific tab CSS; allow tree/listbox `aria-selected` and other non-tab semantics.
- Replace/remove the existing `.administration-tabs button` and `detail-tab` exceptions once their respective causes are gone. Keep the css-coverage shrinking baseline accurate.
- Give the guard *positive and negative fixtures*, so it detects a newly duplicated tab implementation but does not reject legitimate navigation/tree controls.
- Add cross-surface regression tests that render representative pages, assert the shared component/indicator contract, and verify focus, back/forward, deep-link, permission and edit-lock behavior. Capture before/after screenshots at each target width and compare layout, typography, scroll behavior and selected states.
- Run `npm run test:ui`, `npm run test:interaction`, `npm run typecheck`, `npm run lint`, frontend production build, and PR CI. `format:check` is not a blocking gate until unrelated baseline drift is resolved, as documented in `docs/engineering-conventions.md`.
- Exit: a new hand-written tab control fails a focused guard with an actionable explanation; all four existing surfaces pass functional/accessibility/visual reviews.

### DS-5 — Broader component-ownership inventory (follow-on, not a UI rewrite)
- Inventory repeated widgets by role: tab navigation, form fields, messages/notices, filters, dialogs/confirmations, menu/disclosures, action rows, status, empty/loading states, and inspectors.
- Classify each as `canonical`, `intentionally different`, or `unowned/drifting`; record component name, owner, variants, usage locations, checks and migration priority in the UI conventions catalog.
- Cross-reference PR #418 / D1-D6 for forms, notices and control screens; do not create a second competing set of form primitives.
- Prioritize each resulting migration as a separate focused PR (use severity/frequency, not a blanket rewrite).
- Exit: all recurring components have an owner and enforcement strategy; unowned items are tracked explicitly.

## Acceptance criteria

1. Logic, Assets, Administration and Configured Logic details use **one shared tab visual contract** without page-specific tab CSS.
2. Page navigation uses correct link semantics, while in-page details use correct tab semantics; all accessible labels and focus states work.
3. Deep links, browser back/forward, permission-gated items, counts, contextual actions, split-pane selection, Activity loading and revision editing continue to work.
4. Existing shared spacing/typography tokens are used; no raw colors, no new independent theme, no ad-hoc sizes.
5. Selected/hover/focus/disabled states are visibly consistent at desktop, narrow panel, tablet and phone widths; no inaccessible/clipped actions and no unexplained extra scroll area.
6. CI fails on a newly introduced independent tab implementation (guard has negative fixture), and meaningful cross-page interaction tests remain green.
7. A checked-in component catalog and PR design checklist identify the canonical owner of each repeated UI pattern.

## Risks, dependencies and non-goals

- **Parallel changes**: coordinate with the existing control-screens design work (PR #418) and the Logic detail/run-history UI work; rebase each small PR onto latest `develop` before merging. Do not overwrite their ongoing page edits.
- **Accessibility change**: converting anchor `role=tab` to navigational links may require updating tests that search for tab roles. Update assertions to the correct user-visible link semantics, not merely to make tests pass.
- **Editor**: never unmount or reset typed parameters because a data refresh or tab component migration occurred; existing disabled-tab behavior must remain effective.
- **No backend changes**: no changes to API contracts, authorization, database, scheduler, adapter logic or module lifecycle.
- **No speculative framework**: do not introduce a new component library, Storybook, or cross-application design platform just to solve this. Use the existing React/CSS/token and design-preview infrastructure.
- **No unreviewed visual switch**: this plan proposes shared ownership and semantic behavior; it does not claim a newly chosen visual variant is already approved.

## Process

Create DS-1..DS-4 as focused feature branches and draft PRs into `develop`, following `docs/engineering-conventions.md`. Attach code references, before/after captures, accessibility results, existing-test diffs, and relevant acceptance criteria in each PR. Merge only when the latest PR head has green required checks. Keep DS-5 as a documented follow-on audit/work queue rather than expanding the tab changes into unrelated product features.
