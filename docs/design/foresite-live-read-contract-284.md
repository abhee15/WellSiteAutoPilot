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


## Traceability to the actual Oct 26 POC review (#284)

The original eight findings are B1-B8. This mapping supersedes generic gap labels in PR discussion; only the remaining gaps should generate code changes.

| Original finding | Current status | Decision |
| --- | --- | --- |
| B1 unpinned package/object scanning | Done in #285/#286 | No new work |
| B2 independent archive preflight | Done in #285 | No new work |
| B3 synchronous globally locked loading | Done in #285 | No new work |
| B4 temp files / collectible context / per-run instance | Core done in #285; crash-orphan cleanup remains | Track startup cleanup separately; do not build a cache speculatively |
| B5 global ForeSite URL / missing connection identity | Missing in current Gateway read path | Slices A/B |
| B6 assumed ForeSite timestamp and Good quality | Live semantics unverified | Slice C + owner evidence |
| B7 duplicate session handling, error/401/timeouts | Not integrated for current-value reads | Slice B, reuse adapter code |
| B8 no POC CI/live recipe | Package E2E done in #286; ForeSite substitute/live verification outstanding | Slice D |

The broader review F1-F11 also records F4 provider mapping ignored by runtime, F6 lost failed-input observations, F7 duplicated starter freshness policy, F8 publication starvation, F9 one-well View requirement, F10 missing upload UI, and F11 If-Match documentation. F1 package pinning and F5 .NET host are done; F11 describes already-correct UI concurrency and needs docs only. Verify current develop before claiming any remaining F item is absent.

### UI-specific no-duplication decisions

- F9: extend the existing Configured Logic target selector with **direct authorized Asset** as an alternative to View; preserve View selection and authorization semantics. Do not create a second asset discovery or import screen.
- F10: extend the existing logic catalog page with package upload and **separate** trust, publication and runnable statuses; reuse current upload/approval APIs and controls, not a new module-management page.
- F4: do not expose editable provider mapping that is silently ignored. First make the reviewed mapping contract executable or reject unsupported mappings with a clear validation error; reuse the current configuration editor.
- F6: extend existing execution detail/history components with bounded input observations, source time, retrieval time, unit, quality and typed failure reasons once the backend stores them. No independent diagnostics store.
- F7: one effective input freshness policy from reviewed configuration/manifest, consistently enforced by both starter languages and runtime; never use the scratch four-hour workaround.
- UI implementation must follow the matching backend contract, not introduce placeholder frontend models that require rework.

### CI vs live verification

GitHub CI: deterministic recorded HTTP ForeSite substitute, two connections with identical external IDs, simulated auth/cookie/401/error flows, restart/replay, real PostgreSQL/NATS Shadow package execution, and zero writes. No claim of real ForeSite compatibility.

Owner's local acceptance: actual Windows identity, ForeSite endpoint/virtual directory, cookie refresh, sanitized PumpFillage/LastScanTime/LastGoodScanTime responses, observed measurement freshness and quality, UI setup/upload/approval/readiness, Shadow result and failed-input diagnostics. Capture any field semantics that differ from fixtures before relaxing validation.

### Before implementation PR A

Inspect current Server source binding and connection revision models, execution staging and V3 message schema, Gateway service caller permissions, and existing Configured Logic UI. Record exact type/file ownership and backward compatibility in the PR description. Prefer an execution-bound read-plan identifier and authoritative server validation over client-supplied connection settings. Close plan restart/revocation semantics before activating live reads.
