# Asset details: implementation and verification

Scope: asset details only. Hierarchy sticky filters remain deferred. Selecting a member through a saved View uses the same Asset details; this change does not redefine View membership or dispatch semantics.

## Presentation

- Keep name, type, owner and authorized hierarchy path visible. Put identifiers and import provenance in a technical disclosure.
- Explicit grouping nodes (imported-group identity or the reserved group Asset type) show node details and children. Having children alone does not make an operational Asset a grouping node.
- Page authorized configurations five at a time. Pair each configuration with its latest two authorized runs, independently loaded. Keep manual, inactive, previously bound and paused states explicit.
- Show the next future cadence occurrence calculated on the server with the dispatch schedule helper. Times include the viewer's time zone. Runtime eligibility and overlap checks can delay or prevent dispatch.
- Link to run history scoped to the Asset and configuration. Use keyset paging, including the execution's pinned Asset scope rather than today's View membership.

## Live behavior

Reuse the existing page-header LiveStatus and SignalR change signals. Details subscribe to relevant change areas and the existing polling fallback. Reconnection already broadcasts an invalidation to subscribers. Refresh in place without selecting a row or scrolling again. Abort obsolete reads; retain content with a local stale warning on transient failures; clear it on authentication, authorization or not-found responses.

## Verification

Backend: scheduler boundary cases; authorized configuration filtering before paging; explicit group classification; pinned Asset/configuration history isolation and equal-timestamp cursor ordering. Existing API policy inventory includes both read endpoints. No migration or runtime topology change.

Frontend: grouped two-run previews, independent failures, live start/completion/reconnect refresh, selection races, access loss, bounded configuration pagination and scoped history. Run strict typecheck against generated OpenAPI clients, ESLint, node tests and interaction tests. Inspect desktop and narrow viewport rendering before PR review. Required PR CI remains the pre-merge gate.

## Local review results

- Release solution build: zero warnings and errors.
- Generated API strict TypeScript check, ESLint and production frontend build passed.
- Node regression suite after updating from develop: 294 passed. Interaction suite: 450 passed, including polling, accessibility and access-loss checks.
- API contract and Asset-scope security boundary checks passed; Npgsql scoped-query translation and schedule boundary checks passed.
- Chromium inspected at 1440, 390 and 320 pixels: two-run bounds, scoped-history navigation and no horizontal page overflow.
- PostgreSQL-backed asset-detail checks are included in the existing security persistence suite; PR CI is required before merge. Sticky hierarchy filters remain deferred.
