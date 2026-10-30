# Audit read API (first slice)

`GET /api/v1/security/audit-events` returns the audit trail, newest first. This is roadmap step 7 /
UX plan item B8, delivered in two slices.

## Slice 1 (this one): protected administrators only

- **Who:** protected system administrators, through the same `security.manage` policy and SQL re-check as
  the other security directory reads. No new permission and no schema change.
- **Why admin-only first:** audit rows include security changes, reasons and identities, and about half
  of them carry no Asset, so they have no Asset scope to filter by. A per-Asset reader needs a new
  permission key and a decision about which event types an Asset reader may see. That is slice 2 and needs
  owner input.
- **Paging:** keyset on (`occurredAtUtc`, `id`) descending, opaque `cursor`, `limit` 1 to 200 (default 50).
  Pages never skip or repeat rows, even for events with the same timestamp.
- **Filters** (all optional, AND): `actionPrefix` (literal text, `%` and `_` are not wildcards),
  `targetType`, `actorUserId`, `assetId`, `fromUtc`, `toUtc`. Filtering happens in SQL before the page is cut.
- **Not audited:** reading the audit trail does not write an audit event.
- **Response:** `items[]` (id, time, actor, action, target, asset, correlation id, `detailsJson`) and
  `nextCursor` (null on the last page). `detailsJson` is raw recorded JSON text for administrators.
- **Names:** each item also carries `targetName` (the affected record's current name) and `actorName` (the
  person's display name), so the Activity page can say "ForeSite" instead of a GUID. Both are `null` when
  there is nothing to show: the record has no name (an access grant, a run), it was deleted, or the actor is
  a system process. Names are looked up after the page is cut, one query per kind of target on that page,
  and are the *current* name, not the name at the time of the event. Resolved for connections, configured
  logic, logic modules (name and version), assets, asset views, asset types, users and roles. The recorded
  event is never changed or filtered by this lookup.

## Slice 2 (needs a decision)

Asset-scoped readers: a `audit.read` permission, which events they may see (for example only rows whose
`assetId` is within their grant), redaction of `detailsJson`, and the Activity page in the web UI.
