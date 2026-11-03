# Focused run inspector — Option B (Logic Activity and scoped run history)

Status: **Design and implementation plan only; no UI implementation in this change.**
Date: 2026-11-03
Target: `develop`, after a design-first PR review and the normal CI process.

## Decision and user problem

Adopt **Option B: focused, responsive master-detail inspection** for configured-logic Activity, and share its run-history/inspection mechanics with the existing Asset run history and Runs inspector. The selected run must be clearly identified and its details must be visible without scrolling past the entire history. On narrow layouts, the history and selected run replace each other, with an obvious Back control.

The screenshot reported on 2026-11-03 shows a configuration selected in Logic, Activity selected within that configuration, a long run-history list, and execution details below the list and outside the visible area. The repository confirms this shape: `ExecutionHistory.tsx` renders `asset-page-list`, then conditionally mounts `ExecutionDetailPanel` after that list. The button already has `aria-pressed`, but the run row does not have the shared selected-row treatment, and its details are not a sibling pane.

### Why this option

- Three permanent columns (configuration / history / details) sacrifice width needed for technical run inputs, provider provenance, outputs, and diagnostics and are fragile at tablet widths.
- A drawer is useful for quick context but is a poor primary location for long execution details and obscures the workspace.
- A focused two-pane activity workspace preserves configuration identity in the header while giving history and details a flexible 2:3 allocation. The single-pane mobile version follows the existing master-detail convention.
- **Do not invent a new general-purpose UI framework.** Adapt `runs-layout`, `split-panes`, `split-browse`, `split-selected`, `useSplitPaneFocus`, `useSplitPaneViewport`, `ExecutionDetailPanel`, `ActionButton`, `CloseButton`, `Status`, `DetailTabs`, tokens and localization.

This plan complements `logic-details-refinement.md`, `ui-conventions.md`, `control-ui-audit.md` and the UI architecture in `ux-plan.md`. It must not undo their revision lifecycle, security, accessibility, or live-refresh decisions.

## Verified current code boundaries (develop, 2026-11-03)

| Boundary | Current responsibility | Planned change |
| --- | --- | --- |
| `LogicPage.tsx` | Configuration list and selected configuration in outer `runs-layout`; selected configuration from `useHashDetail('logic')` | Coordinate a **focused Activity display mode** so the configuration list yields the content area, without losing selected configuration, filtering state, or editor identity |
| `ConfiguredLogicWorkspace.tsx` | Overview / Configuration / Activity tabs; holds fresh authorized detail and local editor state | Expose Activity focus/exit and selected run intent to the page shell; preserve mounted workspace and editor constraints |
| `ExecutionHistory.tsx` | Shared scoped keyset history; local selected run; renders details below history | Separate browse and selected-run inspection regions **within the same shared component/flow**; add visible selection and robust selection control |
| `executionHistoryApi.ts` | Authorized `/api/v1/executions/history` with `configuredLogicId`, optional `assetId`, keyset `after`, 25-item pages | Keep the API and paging implementation unchanged; no client-side recombination across scopes |
| `ExecutionDetailPanel.tsx` | Fetches authorized selected run, live updates and full diagnostics | Reuse as-is for its data/content and safe refresh behavior; adapt header placement for viewport-aware pinned identity |
| `OperationsPage.tsx` | Full Runs master-detail layout, URL-selected run, highlighted row | Reuse its behavior as the reference; align shared visual affordances rather than duplicate it |
| `AssetRunHistory.tsx`, `assetActivity.ts` | Scoped asset+configuration history and `operations/history?` deep link | Consume the same responsive inspector and preserve the existing asset-history address format |
| `splitLayout.css`, `useSplitPaneFocus.ts`, `useSplitPaneViewport.ts` | Shared pane ratios, single-pane behavior, keyboard focus and viewport measurement | Reuse shared layout rules; any new variant must cover the *full workspace*, not create a nested scroller inside Logic details |
| `routing.ts` | Section/hash-based selections, including `#logic/<id>` | Extend the Logic **detail value** with tab/run state while keeping existing links and tabs compatible |

Current regression fixtures of interest: `logic-details-workspace.test.tsx`, `runs-master-detail.test.tsx`, `split-pane-focus.test.tsx`, `deep-links.test.tsx`, `activity-page.test.tsx`, `run-live-refresh.test.tsx`, `tests/consistency.test.mjs`, `tests/css-coverage.test.mjs`, and `tests/style-tokens.test.mjs`.

## Required UX behavior

### Navigation and information hierarchy

1. Clicking **Activity** on a selected configured logic transitions its workspace into focused run inspection, with the configuration list out of the way. Keep application navigation and the Logic section tabs where applicable, and show a compact context header: **Logic / [configured logic name] / Activity**, the active revision, operating mode, and asset count when supported by the authorized detail.
2. Provide an explicit **Back to configuration** action. Returning restores that configuration, its original list filter and scroll position, and the previously active configuration detail without an unnecessary remount; reopening Activity restores a valid run selection when appropriate.
3. In focused Activity, **run history is the browse pane** and **selected run details are the inspector**. Display the run status as text and symbol, requested time with the existing timezone formatter, operating mode, and shortened execution ID. Avoid redundant configuration identity in every row because it is already in the header.
4. The selected history row uses the established highlighted background and left accent, with a programmatic selected/pressed state. The inspector header repeats the selected timestamp, status, and short ID so identity remains clear when the selected row has scrolled out of view.
5. Initial opening with no deep-linked run selects the newest run from the first authorized history page **once**, after successful load; no selection and an explanatory empty state if there are no runs. Do not automatically switch to a newer run when live data arrives.
6. Changing the selected row updates only the inspector. The details scroll position returns to its top for a newly selected run. The history scroll position and selected row are retained.
7. Provide Previous / Next within the **currently loaded history page**, correctly disabled at boundaries; use existing Older / Newest pagination controls for page changes. Do not pretend to navigate across unknown pages.
8. On paging, filtering, or refresh, keep a selected run's details even if its row is no longer in the current 25-item page. Show a small plain-language note such as **Selected run is outside this history page**, never highlight an unrelated row, and preserve authorized direct-detail access. A dismissed selection returns to history without clearing unrelated configuration state.
9. Reuse the existing run-summary progressive disclosure: summary first; inputs/outputs/provenance and technical IDs available as the existing inspector already provides. Do not build a second run-detail renderer.

### Responsive and scrolling contract

- **Wide/content-sufficient:** two side-by-side top-level regions at the shared approximately 2:3 ratio, with each region scrolling independently under a compact, visible workspace header. Run-history filter/heading and selected-run header stay reachable; do not require scrolling the full run list to see the inspector.
- **Insufficient content width / tablet:** show history first and replace it with selected-run details when a run is opened; Back to history returns to the same history page, selection and scroll position. The shell navigation must not force narrow panes just because the window itself is wide. Prefer the existing token/breakpoint conventions and a *container-aware* responsive decision for the focused area; do not introduce px breakpoints.
- **Phone (320–390 CSS px) and 200% zoom:** single-column, readable labels, no forced horizontal page scrolling, no clipped buttons and no hidden run identity. Long output/code blocks may scroll horizontally inside their own preformatted block only.
- **Scrolling ownership:** avoid `LogicPage` outer selected-pane overflow plus a second `ExecutionHistory` scrollable list and a third detail scroller. In Activity focus, move the run list and inspector to the level of primary scroll ownership, or explicitly disable the parent scroller for this mode. `useSplitPaneViewport` measures the proper content top. Each primary pane gets one scroll region; header is outside the scrolling body where necessary. Verify scroll behavior in a real browser.
- **Focus:** use `useSplitPaneFocus` for the inner run selection. On a narrow transition focus enters inspector; Back restores focus to the selected run (or a safe available fallback). Desktop stays in history unless the user explicitly moves focus. Tabbing, screen-reader labels and native button semantics remain valid.

### Selection, URL and safety contract

- Preserve existing routes: `#logic`, `#logic/<configurationId>`, `#logic/modules`, `#logic/schedules`, `#logic/new:<assetId>`; do not reinterpret these reserved paths.
- Add a deterministic Logic-detail encoding for focused Activity and optionally selected run, e.g. `#logic/<configurationId>?tab=activity&runId=<executionId>` (canonical encoding to be implemented through a single `routing.ts` helper and covered with round-trip tests). A link to an old authorized run must open it even if that run is not in the first history page; the API detail endpoint, not a bounded snapshot, is authoritative.
- Exiting Activity returns to the same configuration and does not send users to the global Runs page. Keep browser Back and Forward coherent without accumulating extra entries for live refreshes.
- Preserve `canReadRuns`, configuration read checks and server-side access enforcement. Never use a list item or cached selection as proof of permission. Clear protected details on 401/403/404 when appropriate, respect access loss in either configuration or run detail, and avoid stale prior-user content when session/selection changes.
- Existing configuration editing rules still hold: tab switching disabled during edit, no loss of unsaved drafts, no remount on authorized live list updates, and read-only or inaccessible state handled explicitly.
- On slow requests, switch selection without showing the previous run as the new one; cancel or ignore racing responses. Keep existing SignalR invalidation/poll fallback and independent list/detail failures. No auto-selection caused by live updates.

## Design-system contracts and implementation constraints

Use **only** shared `ActionButton`, `CloseButton`, `Status`, `DetailTabs`, `LoadingIndicator` and existing search/filter controls where those actions already exist. Introduce no duplicate panel frames, ad hoc button styling, hard-coded colours, unsupported fonts, raw CSS pixel breakpoints, or a second execution history API wrapper. All new strings go through `t()`; times use the application's timezone formatter. Follow `ui-conventions.md`, `control-ui-audit.md` section 5, and the CSS coverage guard.

Preserve the existing globally scoped Runs page's full-list behavior. Align the selected state and inspector controls through shared mechanics, but do not change the Runs list filter meaning or asset/history scope. Preserve exact execution IDs internally; short IDs are presentation only. No new backend service, migration, runtime topology, or generated-client edits are expected.

## Delivery sequence — independent, reviewable PRs

This is a **planning PR only**. Once the plan is accepted, use fresh feature branches from the then-current `develop` and keep each implementation PR draft until its head passes CI.

**Implementation slice 1 — shared run-history master/detail**

- Refactor `ExecutionHistory.tsx` to separate query/selection concerns from browse/inspector layout; reuse `ExecutionDetailPanel` without duplicating detail fetching.
- Add selected-row treatment, obvious empty/failed/loading states, selection retention, current-page previous/next, safe out-of-page selection and proper keyboard support.
- Apply the shared layout to `AssetRunHistory` first as a bounded regression proving the component is truly reusable. Preserve scoped history and legacy `operations/history?` deep links.
- Add structural, interaction, selection race, paging, access denial and responsive tests.

**Implementation slice 2 — focused Logic Activity**

- Coordinate focused Activity between `LogicPage` and `ConfiguredLogicWorkspace`; preserve editor identity across display-mode changes.
- Integrate the shared run inspector as the primary content area, settle scroll ownership and header persistence, add context/back navigation, and implement canonical route parsing.
- Add direct/old-run links, active-revision/live-update preservation, authorization, keyboard/focus and viewport tests. Revisit Runs styling only for actual common behavior gaps.
- Update `ui-conventions.md` with the **focused inspector** rule and add regression guards; do not fork a new generic design system.

**Optional follow-up only if proven necessary:** a separate small PR to normalize any genuinely duplicated selection/navigation logic on other existing list-detail screens. Do not widen the work into a complete app redesign, add new run query APIs, or redesign the module editor as part of this task.

## Acceptance matrix / verification

| Scenario | Expected result |
| --- | --- |
| First visit to Activity with history | Newest authorized run visibly selected once; inspector immediately visible, without scrolling the full list |
| 50+ runs and long technical detail | History and inspector scroll independently on desktop; details do not appear below the whole list |
| Click run A, then B rapidly | B selected/highlighted; B details only; stale A response does not overwrite B |
| Live event introduces newer run | List updates; selected run and its inspector do not jump |
| Next history page | Selected run either visibly highlighted if present or clearly marked outside current page; detail stays valid |
| Refresh/error of history while detail is open | Independent error notice; the selected authorized detail and user context remain unless access is revoked |
| Direct older-run URL outside bounded list | Detail loads via authorized endpoint without first-page membership requirement |
| Permission change, denial or session change | Protected information cleared; no prior user's details displayed |
| Configuration revision changed elsewhere | Existing stale-configuration warning and mutation blocking remain intact |
| Editing a draft | Cannot accidentally switch to Activity or lose unsaved changes |
| 1440/1024/900/768/390/320 CSS px, 200% zoom | No clipping or page-level horizontal scrolling; content-width adaptive single-pane transition with usable Back |
| Keyboard and assistive technology | Visible focus, label for selected run, sensible tab order and Back focus restoration |
| Existing global Runs and asset history | Same scope, deep links, status labels, history paging and existing tests still work |

**Verification per implementation PR:** `npm ci` (if dependencies needed), `npm run typecheck`, `npm run lint`, `npm run test:ui`, `npm run test:interaction`, and the generated API/client/production build per existing workflow. Review current `control-ui-audit.md` guard requirements. Capture **before and after** rendered screenshots for wide, tablet and phone, including selection, empty/loading/error and a long detail, in supported themes. Review Chromium layouts at the specified sizes rather than relying only on mocked geometry. Let the GitHub PR workflow run the full required suite (including PostgreSQL, JetStream, gateway and installer) before marking implementation PRs ready and merging to `develop`.

## Out of scope / explicit non-goals

- No new run-history endpoint, data model, schema, worker or installer changes.
- No redesign of recommendation approvals, configured logic revisions, module authoring or global navigation.
- No automatic navigation to the global Runs page or drawer-overlay dependency.
- No new historical-run filtering/sorting features beyond existing backend capabilities.
- No silent cross-configuration selection or auto-follow of newly arriving live runs.

## Open implementation probes (required before coding slice 2)

1. Prove the existing `useSplitPaneViewport` can measure the focused Activity header/top correctly without relying on a nested `split-panes` selector; adjust the shared hook only if its callers remain correct.
2. Prove the route parser round-trips `new:`, modules, schedules, bare configuration IDs, optional tab/run, and browser Back/Forward without remounting a draft editor.
3. Verify at 900 and 1024 CSS px **with side navigation expanded**, plus 200% zoom, that the history/inspector width threshold uses the actual available area, not the viewport width alone.
4. Confirm the current run-detail header can be pinned by restructuring header/body boundaries without compromising loading, failure, polling or close behavior. This is already a backlog item in `ui-conventions.md`.
5. Confirm the first-page auto-selection happens only once per configuration/focused session, does not fight a direct run link, and never changes in response to subsequent live updates.

The probes should be documented in the first implementation PR, not solved with speculative parallel infrastructure in the planning PR.
