# Loading states

One contract for where and how a loading indicator appears, so the same kind of read looks the same on every page.

## Why this exists

The Access tabs loaded in three different ways: User assignments centered the indicator in the panel, Roles and
Activity put a bare spinner against the panel edge, and Scoped grants indented it inside its own padding. All four
used a shared component; the difference was which one and where the parent put it. The earlier rule
([initial panel loading adoption](panel-loading-adoption.md)) was right but its guard listed seven pages, so pages
outside the list drifted.

## The kinds of loading

| What is loading | Use | Placement |
| --- | --- | --- |
| The first content of a panel, pane or detail view (nothing to show yet) | `PanelLoading` | Centered in the region, inset by the panel gutter, never collapsed below the minimum pane height. Navigation stays usable. |
| A section inside a view that has already rendered (latest runs, schedule, recommendations) | `LoadingIndicator size="small"` | Left-aligned inside the section's own padded container, under the section heading. Does not take over the view. |
| More rows of a list being appended | `LoadingIndicator size="small" inline` | Beside the "load more" action, in its padded row. |
| A refresh while content is already shown | Nothing blocking | Keep the content; the refresh button shows `busy`. |
| A button's own action | `ActionButton busy` | On the button. |
| The whole application opening | `LoadingIndicator size="large"` in the shell | Centered. Only the shell does this. |

A default-size block `LoadingIndicator` is not one of these. It sits wherever its parent puts it, which is the
drift above. The skeleton variant is for list placeholders, not a replacement for the rules here.

## Enforcement

`tests/presentation-ownership.test.mjs` fails when a view adds a default-size, non-inline `LoadingIndicator`.
Views that still have one are listed in `tests/loading-indicator-baseline.json` with a count; the list can only
shrink (a view that drops below its count fails until the baseline is lowered). `loading.css` also gives any
indicator placed directly in a `.panel` the panel gutter, as notices get, so a missed one is never flush against
the edge.

## App-wide review: remaining sites

Each is to be moved to the kind the table says, with a rendered check, and then removed from the baseline. The
treatment below is the proposed one; confirm it when the site is rendered.

| View | What loads | Proposed kind |
| --- | --- | --- |
| `ExecutionDetailPanel` | Run detail, first read | Initial region: `PanelLoading` |
| `KillSwitchPanel` | Kill switch state, first read | Initial region: `PanelLoading` |
| `AssetViewEditor` (editor) | Saved view being opened | Initial region: `PanelLoading` |
| `AssetViewBrowser` | Saved views list | Initial region: `PanelLoading` |
| `AssetBrowser` (search) | Search results | Initial region: `PanelLoading` |
| `ExecutionHistory` | Run history list | Initial region when focused; section read when embedded |
| `ConnectionInventory` | Inventory search | Initial region of the results |
| `AssetDetail` (2) | Logic, recommendations | Section read: small |
| `AssetLogicCard` | Latest runs | Section read: small |
| `ConfiguredLogicRevisionOverview` | Revision overview | Section read: small |
| `ConfiguredLogicSchedulePanel` | Schedule | Section read: small |
| `ConfiguredLogicSetupPanel` (2) | Saved view, target mapping | Section read: small |
| `ModulePackageState` | Package state | Section read: small |
| `UserAccessDetailsPanel` | User access details | Section read: small |
| `AssetViewEditor` (search) | Asset search inside the editor | Section read: small |
| `OverviewPage` | Attention list | Section read: small (already in a padded body) |

The real-browser loading audit (`tools/design-preview/audit-loading.py`) covers Connections, the user directory
and Status. Extending it to every page above, with measured geometry, is part of the same work.
