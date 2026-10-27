# Execution detail provenance

The existing authorized execution-detail response adds an optional provenance
projection: the pinned package SHA-256 and up to 64 saved connection/revision
summaries with distinct Asset counts. It is read from that execution's immutable
V3 snapshot, not today's catalog, connection settings or View membership.

The normal list and legacy V1/V2 response behavior is retained. Provenance parsing
runs only for execution detail and fails closed for malformed/oversized snapshots.
Read-plan IDs, credentials/settings, provider mappings, reviewed manifests and
server-only authority never enter this DTO. Existing execution scope checks
remain the authorization boundary; no new endpoint or permission is introduced.

ExecutionDetailPanel uses its existing summary layout to show the pinned hash
and saved connection revisions. The frontend defensively bounds this optional
projection, and a truncated connection list is explicit. API contract tests cover
immutable grouping and private-field exclusion; UI tests verify detail rendering
and malformed projection rejection. Generated clients are regenerated from the
Server OpenAPI contract, not hand-edited.
