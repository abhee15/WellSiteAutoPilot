# ADR-008: Live updates are a coarse change signal, never data

## Status

**Proposed.** Needs owner approval. The hub itself waits for the Windows Negotiate field data in issue #92.

## Context

The architecture baseline names SignalR; nothing is built and the UI polls every 10 seconds. A push path
that carried data would need its own copy of the asset-scope filtering that REST already applies in SQL
before pagination, and could reveal an Asset a person may not see.

## Decision

1. **One change-notification seam** on the Server, raised after a transaction commits. Producers name an
   area only: `executions`, `recommendations`, `configured-logic`, `assets`, `modules`. The seam carries
   no identifiers or values.
2. **The hub sends only the area name**, and only to connections whose user currently holds that area's
   read permission. The browser then refetches through the existing authorized REST endpoints, so scope
   filtering stays in exactly one place.
3. **Polling remains the fallback.** The client shows connection state and data freshness, and keeps
   polling while disconnected or when the hub is unavailable.
4. **Permission changes reach live connections.** The Server re-checks the area permission on every
   reconnect and at most every 60 seconds, and closes the connection when it is no longer held. The
   interval is a setting with this default.
5. **Authentication follows #92.** Negotiate on Kestrel is connection bound and WebSockets cannot add an
   `Authorization` header, so the hub is wired only after real-browser behaviour is known. The seam is
   built first and is independent of it.
6. Notifications are best effort. Missing one must never be a correctness problem, because polling and
   the next refetch recover the state.

## Consequences

- No second filtering path exists, so a push cannot widen access.
- A burst of changes can be coalesced per area without loss, because only the area is sent.
- Server scale-out (not in scope today) would need a backplane for the seam.
