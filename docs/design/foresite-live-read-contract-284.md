# ForeSite live-read contract — first implementation slice (#284)

Status: design gate, not production enablement. Based on the 26 Oct 2026 POC review; does not revisit completed asset discovery or Views.

## Verified baseline
- `Worker.DotNet/Execution/GatewayDataClient.cs` sends providerId, assetExternalId and quantity, but not saved connection identity or revision.
- `IntegrationGateway/Program.cs` exposes `GET /api/internal/v1/data/current` under DataReadPolicy and routes to simulator by default.
- Approved .NET package hosting and pinned V3 dispatch are already merged (#285, #286, #287). Do not duplicate package loading or broker work.
- The POC's global ForeSite URL and guessed measurement freshness/quality must not be promoted as-is.

## Contract decisions and security invariants
1. Server resolves each configured input's local AssetId to its authorized SourceBinding: ConnectionId, ResourceKind, ExternalId, owner, adapter key and reviewed connection-settings revision. Never use a display name or a module-supplied URL as routing identity.
2. Before execution, Server authorizes the selected asset/connection and issues a **bounded, opaque read-plan reference** for the Gateway. The reference binds execution ID, allowed quantities, external IDs, adapter/connection identity and settings revision. Credentials stay in the Server/Gateway trust boundary; no connection secret enters the worker message or module.
3. Gateway accepts reads only for the execution-bound plan, rejects missing/expired/mismatched plans and disallowed quantities, and selects the matching saved ForeSite connection. No global localhost fallback. The existing simulator route remains explicit for simulator executions, not a fallback for ForeSite.
4. Decide plan transfer and recovery before coding: prefer an authenticated Server-to-Gateway prepare operation plus short-lived Gateway plan store **only if** restart, idempotency, revocation and multi-Gateway behavior are specified. Compare against a versioned settings projection; do not implement both.
5. Disablement/settings revision/source-binding changes invalidate new reads according to an explicit queue/start policy. Accepted pinned package bytes do not authorize stale connection access.
6. Reuse ForeSite adapter's Windows identity, cookie/token lifecycle, one 401 refresh, JSON response validation and FullErrorMessage handling. Bound the total timeout and propagate cancellation.

## PR slices
**A — contract + authorization tests:** versioned execution read-plan DTO and Server-side source/connection resolution; deny forged cross-connection/external IDs, wrong tenant/scope, unknown quantity and changed revision. Keep old simulator V2 behavior explicit. No live provider registration.

**B — Gateway routing:** authenticated plan preparation/lookup, per-connection ForeSite sessions, virtual-directory base URL normalization, two ForeSite servers with the same external well ID, no localhost fallback, concurrent 401 coalescing and malformed/error response tests. No provider writes.

**C — measurement provenance:** obtain sanitized live response fixtures for PumpFillage, LastScanTime and LastGoodScanTime. Preserve measurement timestamp separately from retrieval timestamp, source timezone and unknown quality. Reject stale/missing/invalid data; never silently stamp UtcNow as measurement time.

**D — failure evidence + Shadow acceptance:** persist bounded, authorized input observations and rejection reasons even when evaluation fails; keep failed module outputs excluded. Test PostgreSQL/NATS/approved package/worker/recorded ForeSite HTTP substitute end-to-end, with zero provider writes. Owner-run Windows/ForeSite live verification remains a separate gate.

## Acceptance for slice A
- Same external well ID on connection A and B resolves to distinct immutable source identities.
- Worker cannot change connection, external ID or quantity by altering its request.
- Disabled, unauthorized, unknown and stale-revision sources are rejected before live reads.
- Secrets never appear in execution messages, audit input diagnostics or module SDK.
- V2 simulator tests remain green; V3 request/version behavior is explicit.
- Tests and CI pass before requesting merge.

## Open decision
Choose durable recovery semantics for Gateway read plans (Gateway restart and multiple instances) and exact disablement policy. This document deliberately does not pretend those decisions are already implemented.
