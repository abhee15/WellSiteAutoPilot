# Shared search controls and grants form refinement

## Research and repository audit

The owner requested research, planning, a visual/interaction probe and review before production implementation.
The screenshot shows uneven control heights, tightly packed labels, a detached search action and no clear
spacing between selection, lookup and results. Existing design tokens already define 36px standard
controls, 44px touch targets, spacing, focus rings, colors and a readable content width. Reuse these.

Primary references:
- https://www.w3.org/WAI/ARIA/apg/patterns/button/ — native keyboard activation and accessible button names.
- https://designsystem.digital.gov/components/search/ — labelled inputs, native submit behavior, small icon and text-action variants.
- https://www.carbondesignsystem.com/building-blocks/core/components/search/specifications — consistent sizes/alignment.

## Inventory and migration map

There are 17 native search inputs plus the AD lookup. Migrate their presentation together:

| Consumers | Existing behavior to preserve |
| --- | --- |
| AssetInventoryControls, AssetsPage, AssetViewEditor | Caller-owned criteria/filtering, permissions and selection |
| ConnectionInventory (wells and facet values) | Existing discovered-data filtering and selection |
| LogicPage (two), LogicCatalogControls, ConfiguredLogicControls | Existing catalog/configuration criteria |
| RecommendationInboxControls, OperationsViewControls (two) | Criteria, saved views, keyboard and labels |
| RoleCatalogPage, ScopedGrantEditor | Local role/Asset filtering and validation |
| ActivityPage (two) | Draft filters applied through the existing action |
| DirectoryGroupPicker | Explicit lookup, native Enter submission, two-character minimum and busy/disabled state |
| ConnectionSourceChanges | Explicit lookup inside the connection edit form; existing Enter handler and type=button action |

ConnectionSourceChanges is rendered inside ConnectionsPage's form. A common control must not introduce
nested forms or unexpectedly submit the connection editor. Existing asynchronous handlers, cancellation,
debouncing, authorization and response validation remain owned by their consumers.

## Component contract

1. ActionButton: native button with a required localized accessible label, optional Lucide icon,
   icon-only or icon+text presentation, standard/large sizes, variant and disabled/busy state.
   Default type is button; callers explicitly opt into submit. Reuse the existing spinner style.
2. SearchInput: native search input props (value/onChange, labels, refs, IDs, length bounds,
   keyboard handlers, placeholders and disabled state), decorative leading search icon for filters,
   and optional trailing ActionButton for explicit searches. It owns no form, network, cache or criteria.
3. Standard size follows existing toolbar controls; large size uses the existing touch-target token.
   Icon-only actions keep a screen-reader label and tooltip. Visible text remains available where helpful.
4. Directory and source-change lookups use the same search action. Conventional refresh actions in
   grant/role administration also use ActionButton. Assignment/review actions retain visible labels.

## Form layout

Grants selection, role/scope editing and review share one readable measure. Use a consistent spacing
rhythm for panel header, introduction, labels, controls, help and results. Align field and action height.
The directory search action sits against the input, with results separated below. Keep the desktop
layout comfortable and the 320/390px layout free of overflow. Use current light theme and tokens.

## Probe, review and implementation gates

Before changing production components, render a disposable browser probe using real application tokens:
- filter input; icon-only lookup; icon+text lookup; disabled and busy variants;
- full grants selection layout at desktop and narrow width;
- keyboard Enter/Space, labels, focus visibility, action/input heights, and no unintended form submit.
Review screenshots and geometry; adjust the contract before migrating callers.

Then implement the reviewed primitives, migrate the inventory, run existing interaction/logic tests and
add focused shared-control tests for Enter/click, busy guards, accessible names, ref forwarding and outer
form safety. Inspect actual React screens after migration, run frontend lint/build and repository checks,
then publish a draft PR targeting develop. Merge after current-head CI passes.

## Pre-implementation probe review

The disposable HTML probe used the current tokens, styles and bundled Roboto fonts.
At 1440px and 390px the field/action heights matched (44px), focus outline was 2px,
Enter submitted once, Space activated the icon action, and the narrow page had no
horizontal overflow. The embedded source-search probe executed its own handler via
Enter/click without submitting the outer connection-edit form. Screenshots were
inspected before production edits. The reviewed design uses a unified input/action
edge, a decorative icon only for immediate filters, 24px stage spacing and visible
labels above fields. Large touch sizing is selected by the form; toolbar searches
retain the standard application density.

## Post-migration browser review

The real React grants selection and role/scope editor were rendered with fixture data,
using the bundled Roboto fonts and the full application CSS. Desktop and 390px screens
were inspected; the search field and icon action both measured 44px, there was no
horizontal overflow and the browser reported no page errors. Representative filter
toolbar and text-action variants were inspected under the existing toolbar selectors.
The review caught the grants select background shorthand removing the shared chevron;
using background-color preserves the existing dropdown affordance. Role/scope fields,
Asset search, panel headers and review actions now follow the same spacing and sizing.
Temporary visual harnesses and generated API clients are excluded from the change.

The shared ActionButton was also probed as a standalone toolbar action, outside SearchInput.
Its primary/secondary/disabled styles and icon sizing are owned by the component so
legacy toolbar selectors cannot replace the chosen variant or stretch an icon action.
