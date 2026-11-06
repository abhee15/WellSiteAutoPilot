# Recommendations workspace

Tracking: #469. This follows the shared fields/notices foundation in #470.

## Review and choice

The current page stacks a global kill-switch panel, all unconfirmed commands, three metric filters, a run selector, proposal table and decision form. The fixed minimum pane height can exceed the space remaining below those sections. The same approval-versus-execution guidance and proposal facts appear repeatedly.

Keep the existing responsive inline inspector for recommendation decisions. Comparing proposals and retaining list position are useful while reviewing, so a modal overlay would interrupt the main task. Fluent 2 recommends inline drawers when both main and contextual content need to remain interactive; Carbon recommends a dedicated surface when expanded table content becomes cramped.

Use the shared navigation strip for three addressable tasks: Review recommendations, Unconfirmed commands and Command controls. Preserve existing `#recommendations/actions[/id]` addresses; reserve `#recommendations/settings` for command controls. The command state remains visible in a compact summary on the review workspace, and unresolved command counts/errors remain visible with a destination to their workspace. Moving controls never changes their permissions, confirmation or server gates.

The review browse pane owns its heading, summary filters, search, run selection and proposals. The inspector owns the selected recommendation and its existing decision/execution flow. Summary filters no longer consume fixed space above both panes. On narrow screens the inspector replaces the browse pane and Back returns focus to the originating Review action. Global navigation remains available.

## Scroll contract

At desktop widths the workspace fills the page-body content box using available flex space. Browse and inspector scroll independently; neither enforces a minimum taller than the available viewport. Auxiliary task content has one scroll owner. At tablet/phone widths the active task uses the existing page-body/document scroll owner, with no nested fixed-height panes. Short viewports fall back to normal task scrolling rather than clipping actions.

## Verification and iteration

Render before/after populated pending, approved, expired, read-only, unconfirmed-command and control settings states. Inspect 1440/900/420/320 widths and a short desktop viewport. Measure scroll owners and horizontal overflow; test list position, focus on narrow open/Back, direct links and browser Back, filters, delayed writes, retained reason after error and separate approval/execution. Retain the existing bounded data windows and honest count wording.

Primary guidance:
- https://fluent2.microsoft.design/components/web/react/core/drawer/usage
- https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines
- https://atlassian.design/components/drawer/usage/

The broader page-by-page typography work remains #468; this change uses existing semantic tokens and shared visual owners.

## Rendered iteration

The first iteration moved the large metric cards into the browse pane, which exposed a second problem: the cards stacked and displaced proposals in a narrow inspector layout. The second iteration adds an owned compact SummaryCard variant, a labelled native run selector and one scoped data-window caption. At very narrow pane widths the metric filters become single-line rows rather than squeezing their labels. Repeated decision metadata moves into a native disclosure while asset/proposal/value, logic/run links, relative expiry and reason remain in the visible brief. The approval-only notice remains next to the decision controls.

The local mock browser review covers 65 before/after cases, including a four-command unresolved queue. Desktop outer/document scroll is explicitly measured; short desktops use the outer task scroll instead of enforcing a minimum pane height. Narrow detail/return focus, metadata disclosure, direct task routes and browser Back are checked. Existing delayed-write/refusal checks continue in the shared-form audit. These are representative mock datasets; live provider behavior is not asserted by a visual review.
