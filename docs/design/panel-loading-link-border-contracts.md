# Panel loading, links, borders and alignment

Tracking: [#463](https://github.com/abhee15/WellSiteAutoPilot/issues/463). Extends the [presentation review](presentation-consistency-review.md) and the existing D1 ownership in [control UI audit](control-ui-audit.md).

## Why the previous checks missed drift

Tokens limit the available colors and spacing values, but do not decide who owns a visual pattern. A page can use a valid token to override another component's minimum height or indentation. A CSS-coverage test proves that a class has a rule, not that the rule belongs there or behaves consistently in context. The action review demonstrated both failures: compact navigation in a toolbar and legacy/page CSS overriding touch geometry.

## Loading behavior

| Situation | Shared presentation | Interaction |
| --- | --- | --- |
| Initial list or changed-record detail, no usable content yet | `PanelLoading`: centered indicator and descriptive text inside the affected content panel | Replace unavailable content; keep navigation and Close usable. No page-wide overlay or dialog. |
| Background refresh with usable data | Existing content remains visible; the refresh action shows busy state | Do not replace a populated panel with a spinner or reset selection/scroll. |
| Save, approve, execute or another mutation | Busy state where the action starts, with affected controls disabled by their existing transaction state | No duplicate submission; no fake global lock. A consequential workflow may need its own reviewed blocking contract. |
| Small dependent field/status read | `LoadingIndicator` in content or inline placement | Keep feedback adjacent to the operation. |
| Known stable content shape | Existing skeleton variant may be used | Spinner/skeleton choice must not change the horizontal gutter. |

The existing `LoadingIndicator` remains the single animation/announcement owner. `PanelLoading` composes it, rather than introducing another spinner. The indicator retains its short appearance delay and reduced-motion behavior. The centered state is a panel state, not a modal and not a fixed overlay attached to the browser window.

First migration: Assets initial content, Logic module/configuration lists, Runs list, selected configuration detail, and recommendation initial/run content. Older screens still need the same placement review; this is not a claim that every loading call site has been migrated.

## Link hierarchy

- `ActionLink`: native navigation alongside action buttons, with the existing action sizing and variants.
- `ContextLink`: standalone record/drill-down links in summaries, cards, control rows and the user menu. Blue text and a directional icon identify navigation without adding a bordered pill inside every card. Use the control text size, visible keyboard focus and touch sizing. All former raw `.action-link` consumers now use this owner.
- Inline prose links retain ordinary native anchors and inline-link treatment. Buttons remain buttons for filtering, selection and mutation. Tab navigation remains under `TabStrip`.

The global `.action-link` visual rule has moved to `components/contextLink.css`; feature CSS may position the link with layout margins, but may not change its font, padding, border, color, animation or target height.

## Border roles

| Surface | Treatment |
| --- | --- |
| Input/select/action boundary | Shared control border token; recognizable against the surrounding surface |
| Passive panel/card frame | Subtle panel border; do not darken every nested box to match controls |
| Table/list separator | Subtle row divider; do not add another full frame around each row |
| Interactive recommendation run selector | Control border, because it is an input choice; selected color/background and `aria-pressed` communicate selection |
| Keyboard focus | Shared focus ring, separate from the persistent selected-state treatment |
| Validation/error/warning | Explicit message and semantic state treatment, not border color alone |

This slice corrects recommendation run selectors that used a passive-panel border despite being buttons. It does not remove safety notices, change control policy, or indiscriminately darken borders throughout the application. Shared fields/notices/confirmations and the remaining raw form migrations continue under D1/#418, with one implementation owner.

## Alignment and spacing

`--space-panel-inline` names the shared panel gutter. Panel headers, edge table cells, filter toolbars, hierarchy content, loading placement, recommendation headings/rows and control rows use it. Narrow-screen filters no longer silently reduce their gutter to 12px while neighboring content remains at 18px. Internal icon, field, group and section gaps continue to use the existing spacing scale; consistency does not mean identical spacing for unrelated hierarchy levels.

Skeletons and spinners default to content placement without an extra horizontal gutter. The optional `inset` flag is for unpadded containers. `PanelLoading` owns its centered panel padding. Padded nested components must not add another panel gutter themselves.

## Prevention and evidence

- `presentation-ownership.test.mjs` rejects visual overrides of shared action, tab, contextual link and loading classes outside their CSS owner; positioning-only margins remain allowed. Synthetic negative cases cover the actual toolbar minimum-height and loader-padding regressions.
- New raw contextual-link and spinner markup outside the shared owners fails the guard.
- One narrow legacy exception remains for inline Copy controls in `styles.css`. It is explicit and cannot cover other actions; remove it when compact-copy density is migrated to its own owner.
- Native link destinations, access filtering and user-menu dismissal remain tested. No JavaScript-only links or fake button roles are introduced.
- Browser fixtures cover centered initial loading, keeping content during delayed refresh, local form blocking during a delayed save, tab visual parity, focus, touch targets, and no page overflow at 1440/900/420/320px.
- Source guards complement browser checks. They cannot prove that every screen's information hierarchy, wording or border role is correct; those require rendered review and behavioral tests in each migration.

## Guidance

- [Carbon loading pattern](https://www.carbondesignsystem.com/building-blocks/core/patterns/loading): choose feedback based on initial loading, local work or truly application-blocking operations.
- [Carbon inline loading](https://www.carbondesignsystem.com/building-blocks/core/components/inline-loading/guidelines): preserve placement and keep short local work next to its initiating action.
- [Carbon links](https://www.carbondesignsystem.com/building-blocks/core/components/link/guidelines): standalone versus inline navigation and restrained link density.
- [Atlassian borders](https://atlassian.design/foundations/border): distinguish boundaries, selection and focus through semantic width/color roles.
- [Atlassian spacing](https://atlassian.design/foundations/spacing): group by relationship and proximity, using an intentional scale and shared layout rules.

These principles are adapted to the existing light WSA interface and components; no additional UI library is introduced.
