# UI medium items: plan

Status: planning only. Nothing here is implemented. It follows the small items (error boundary, logical
CSS properties, shared delete confirmation) and comes before the theming program in
`docs/development/roadmap.md` ("dark and high-contrast themes").

Order matters: item 1 is a prerequisite for theming, because a dark palette cannot be accepted without an
automated way to prove it is readable.

## Decision recorded from the small items

`ConfiguredLogicSetupPanel` keeps its native `<select multiple>` for choosing assets. The shared `MultiSelect`
is built for short filter lists: it has no scrolling or search, its menu is absolutely positioned, and "nothing
ticked" means "no filter". The setup list can hold hundreds of wells, where that would break. Saved views are
the scalable path and already exist. Revisit only with a searchable picker, as its own design. The baseline
entry in `tests/legacy-form-notice-baseline.json` stays.

## 1. Contrast and real-browser accessibility gate

**User problem.** Low-contrast text is unreadable on a control-room screen in glare. Today nothing checks it:
`tests/interaction/axe.ts` disables `color-contrast` and `region` because jsdom has no layout or paint.

**Facts (checked).**
- `src/tokens.css` has 55 semantic `--color-*` tokens. All resolve to plain hex, so contrast can be computed
  in Node without a browser.
- A real-browser harness already exists: `scripts/visual-run-history.mjs` drives Chromium over CDP against
  `tests/visual/*.tsx` pages, and CI runs it (`test:visual:run-history`, `test:visual:adapter-operations`).
- Brute-forcing every text token against every surface token is meaningless (most pairs never occur), so the
  check must be driven by a declared list of real pairs.

**Design.**
1. `tests/contrast-pairs.json`: each entry names a foreground token, a background token, the context
   ("body text on panel", "disabled label on input") and the required ratio (4.5 text, 3 for UI boundaries
   and large text). A Node test computes the ratio from `tokens.css` per theme block and fails below the
   requirement. A second guard fails when a `--color-text-*`, `--color-border-control` or `--color-focus-*`
   token is neither in the pairs file nor in an explicit exempt list with a reason, so new tokens cannot
   skip the check.
2. Real-browser axe: inject the pinned `axe-core` into the existing visual pages through CDP with
   `color-contrast` and `region` enabled, and fail on violations. Runs per width today and per theme once
   themes exist.

**PR slices.**
- 1a: pairs file, computing test, completeness guard. First run measures the current light theme. Any token
  that fails is fixed in this PR with before/after screenshots for owner review (a value change is a
  visible change).
- 1b: browser axe in the visual harness.

**Risks.** Fixing failing tokens changes the look; mitigated by screenshots before merge. Axe in a real
browser can flag third-party-free markup we have not seen before; fix or exempt each with a written reason,
never blanket-disable a rule.

## 2. Sticky table headers

**User problem.** On long Runs, Activity and Asset lists the column names scroll out of view.

**Facts (checked).** Three sticky rules exist (side navigation, filter toolbar, run-history header). Tables
sit in `.table-wrap { overflow-x: auto }`. An element with `overflow-x: auto` becomes the scroll container
for its sticky children, and it does not scroll vertically, so a `position: sticky; top: 0` header inside it
will not stick to the page scroll. A naive `th { position: sticky }` therefore does nothing.

**Slice 0 (decide before building).** Audit which tables are actually long and which need horizontal scroll
at narrow widths, and check whether `DataTable` has a stacked layout on phones. The design depends on it:
- If a table never needs horizontal scroll at the widths where it is long, make the wrapper
  `overflow-x: clip` at those widths so sticky resolves against the page.
- If it does, a header stuck to the page is not possible without a nested vertical scroll region, which harms
  keyboard and touch use. Then the answer is "no sticky header for that table", not a hack.

**Design.** Opt-in `stickyHeader` prop on the shared `DataTable`, token for the offset below the sticky
filter toolbar (stacked stickies must not overlap), `z-index` from one documented scale.

**Verification.** Real-browser scroll test (extend the visual harness: scroll, assert header top equals the
expected offset), plus screenshots. One PR after slice 0.

## 3. Unsaved-changes guard

**User problem.** An operator edits a connection, asset type or grant, clicks a sidebar link or presses Back,
and loses the work with no warning.

**Facts (checked).** Navigation is hash routing (`routing.ts`). There is no `beforeunload` anywhere. Only
`ConnectionsPage` knows whether it is dirty (`isDirty(connection, adapter, draft)`); other forms track
nothing.

**Design.**
- `useUnsavedChangesGuard(dirty: boolean)`, one shared hook. "Dirty" must mean "differs from the loaded or
  last-saved values", not "the person touched a field", so a reverted edit is clean again.
- Reload and close: a `beforeunload` handler registered only while dirty (the browser shows its own prompt;
  text cannot be customised).
- In-app navigation: a hash change cannot be cancelled, so route every in-app link through one function in
  `routing.ts` that asks registered guards first. For Back/Forward and edited URLs, restore the previous hash
  and ask. The question is the shared `ConfirmPanel` (focus moves to it): "Leave without saving?" with
  Stay (default) and Leave.
- No autosave and no draft storage. Both raise conflict and stale-revision questions the backend contract
  (revision checks, ADR-004) does not currently answer.

**PR slices.** 3a hook, routing integration and `ConnectionsPage` (it already has the dirty definition).
3b the other editors, grouped by area, each defining dirty against its own initial value: asset type
editor, asset creation and view editor, grants and roles, autonomy policy, logic setup.

**Tests.** Unit: hook registers and removes the listener, dirty reverts to clean. Interaction: Stay keeps the
typed values and the page, Leave navigates, sidebar link, Back button, save then navigate asks nothing.

**Risk.** Prompting on clean forms trains people to ignore the prompt, so the dirty definition is the part
that needs the most care and its own tests.

## 4. Prettier baseline

**Facts (measured).** `npm run format:check` exists but CI does not run it, and there is no Prettier config.
With defaults it reports 197 files. Setting `--print-width` to 120, 160 or 200 still reports 196 files, so
the mismatch is quote, wrapping and layout style, not line length. `.editorconfig` asks for 4-space
indentation while the code uses 2. Adopting it means reformatting about 196 files in one change, which also
buries `git blame`.

**Recommendation (needs an owner decision).** Do not do a whole-repo reformat now. Options:
- A. Leave formatting to ESLint plus review, and remove the misleading `format:check` script.
- B. Adopt a Prettier config that matches the dominant style, then enforce `prettier --check` only on files a
  PR changes (no mass reformat; files converge as they are touched).
- C. Whole-repo reformat as one isolated commit listed in `.git-blame-ignore-revs`.

B is the safest way to get consistency. It is deliberately last in the order because it changes no behavior.

## Suggested order and effort

| # | Item | PRs | Depends on |
| --- | --- | --- | --- |
| 1 | Contrast and browser axe gate | 2 | none; blocks theming |
| 3 | Unsaved-changes guard | 2 to 3 | none |
| 2 | Sticky headers | 1 after audit | benefits from 1b (real-browser checks) |
| 4 | Prettier | 0 to 1 | owner decision |

## Open questions

1. Prettier: A, B or C?
2. Unsaved-changes: is "Stay / Leave" with no draft saving acceptable, or are drafts a requirement?
3. Which are the longest lists operators actually scroll (Runs, Activity, Assets)? That sets sticky-header priority.
