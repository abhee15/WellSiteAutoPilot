# Asset identity presentation audit

The operator-facing identifier is the **Asset name** and, when authorized and known, the **owning Connection name**: `RPOC_00002 (ForeSite Production)`. The immutable Asset ID remains the identifier in persisted bindings, commands, links, scope checks, provenance and audit records. Do not derive identity by searching for a same-named well.

## Reviewed UI surfaces

| Surface | Decision | Implementation or follow-up |
|---|---|---|
| Logic / Your logic grid | Show the active revision's target assets, never a union of historical revisions. Fall back to the latest saved revision only when there is no active revision. Show up to two names and an accurate remainder count. | `configurationTargetAssetIds`, `configuredTargetLabel` |
| Logic / selected revision overview | Show named assets with owner, using the selected configuration's saved bindings. | `ConfiguredLogicRevisionOverview` |
| Logic / revision target and provider bindings | Name each source target; retain provider IDs, roles and requirement IDs as distinct technical concepts. | `ConfiguredRevisionDetails` |
| Logic / revision editor | Use name and owner in choices, overrides and data binding headings, while the select `value` remains the unchanged Asset ID. | `ConfiguredLogicSetupPanel` |
| Logic / Schedules | Only the live revision's bound assets are relevant to a schedule, with owner-aware names. | `scheduleOverview`, `SchedulesOverview` |
| Recommendations (review/execute) | The asset can be outside the initial snapshot. Use authorized asset resolution to show name and owner when available. The original ID stays in the detail tooltip. | `RecommendationsPage` |
| Overview / Latest runs and Runs / Operations | A configured execution can intentionally have no single Asset ID. Use its saved revision context; do not invent one. | PR #419 addresses blank configured-run labels. PR #421 resolves the run's saved revision targets by name and owning Connection, including out-of-snapshot authorized lookups; reconcile the shared helper when merging. |
| Assets inventory, tree, and views | Already use names for primary navigation, with Connection owners where a hierarchy needs disambiguation. | No replacement of navigation IDs |
| Access grants / scoped administration | Use authenticated grant-directory names where provided; keep exact IDs in grants for security decisions. The grant directory does not currently return owner Connection identity. | PR #421 adds optional Connection identity in the protected grant directory, displays owner-qualified names across selection and confirmations, and retains exact grant IDs and security checks |
| Execution technical details and audit events | Exact IDs and source identities are necessary for traceability. | PR #421 complements selected audit Asset IDs with authorized names and Connection context while keeping exact recorded IDs and raw evidence visible |

## Safety and scale

- The workspace snapshot is capped at 100 assets. Missing from it **does not** mean an asset is absent, and must not cause the UI to show an incorrect name.
- Resolve extra labels using the existing asset-scoped `GET /api/v1/assets/{id}` and `GET /api/v1/assets/owners` APIs only with `assets.read`; permission revocation and live asset/connection changes invalidate the resolved display data.
- A missing, denied or failed lookup keeps the shortened ID, not a fabricated name. IDs remain in the API payload and links.
- Bound lookups to the first two displayed targets per configuration, with extra selected-revision targets capped and a total lookup bound of 100 IDs. Limit concurrent reads to four. This avoids fetching all 1,000+ wells solely to display a summary.
- If large installations show appreciable latency under this bounded fallback, add a dedicated **batched, asset-scoped identity endpoint** with a bounded request size and a SQL authorization predicate before result projection. Do not load the entire asset directory or issue an unbounded request fan-out.
- If two assets share name and owner in the currently known authorized set, append a short ID discriminator to avoid ambiguity.

## Action placement

`ConfiguredLogicWorkspace` had two separate rendering paths for actions: the loaded header used an icon-only Close while its error/empty path rendered a labeled Close button at the bottom, and Refresh sat separately in `setup-actions`. A single header action group now owns the icon-only Refresh and Close controls across the normal and failure states, using design tokens for spacing. Error handling itself is unchanged.

## Checks

- Pure labeling tests: live/draft/history revision selection, duplicates across and within Connections, unknown identity fallback.
- Interaction tests: authorized out-of-snapshot name resolution, no directory calls without `assets.read`, configuration editor labels, and header alignment in load failure.
- CI checks on the draft PR; manual browser review in an environment with imported wells remains important.
