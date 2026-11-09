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

`tests/presentation-ownership.test.mjs` fails when any view uses a default-size, non-inline `LoadingIndicator`.
There is no exception list: a view either uses `PanelLoading`, or a small or inline indicator. `loading.css` also
gives any indicator placed directly in a `.panel` the panel gutter, as notices get, so a missed one is never flush
against the edge.

## How the existing sites were sorted

Every view that had a default-size loader was checked in context:

| View | Where it loads | Kind used |
| --- | --- | --- |
| Roles, Activity, Scoped grants, User assignments | First content of the panel | `PanelLoading` |
| Run detail (`ExecutionDetailPanel`) | First read of the detail pane | `PanelLoading` |
| Run history, focused | First rows of the list pane | `PanelLoading` |
| Run history, embedded in an asset page | Section of the asset page | Small |
| Asset detail (logic, recommendations), latest runs, revision overview, schedule, package state, user access details, view editor (assets, search), logic setup (view, mapping) | A read inside an already-rendered section | Small |
| Asset tree, saved-view tree, asset search, connection inventory | Rows being added below an existing list | Small |
| Kill switch strip, Overview attention | A compact strip that already has its own padding | Small |

## Automated checks

- `tests/presentation-ownership.test.mjs` rejects a default-size block loader in any view.
- `tools/design-preview/audit-loading.py` exercises delayed reads and terminal states on Connections, the user
  directory and Status.
- `tools/design-preview/audit-loading-pages.py` holds every data read on every route (all sections, the four
  Access tabs, asset and logic details) at desktop and phone widths and measures each indicator: inside a panel it
  must be inset by at least 12px, a first-content loader must be centered in a region of at least 160px, and nothing
  may overflow horizontally. It runs in the tab visual review workflow and keeps its measurements as JSON.
