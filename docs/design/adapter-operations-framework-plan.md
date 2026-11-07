# Adapter Operations framework — implementation plan

Status: approved design direction; implementation in progress  
Tracking: #484  
Base: `develop`  
Branch: `feature/adapter-operations-framework`

## Product boundary

The adapter Operations explorer describes **what the installed adapter can do**, while a connection-specific inspector describes **what this connection and asset may do**. Asset discovery remains available to connection import workflows, but is **hidden from the Operations UI**. Module authoring is deferred. The page must not issue control commands.

## UI contract

Under Connections > Adapters, present an adapter list and searchable operations list with a persistent selected-operation detail panel. Details include overview, typed request/response contract, quality and timestamp semantics, bounds, compatibility, and connection-specific availability when selected. On narrow screens, switch to list/detail navigation rather than compressed columns. Respect shared tabs, selection states, scrolling, loading/empty/error patterns, and accessible keyboard navigation.

Do not hardcode a ForeSite list of operations. Render only descriptor-declared operations. Discovery remains a private capability for import, not an operation row. Do not advertise an alarm, setpoint, or daily-average read until a working adapter implementation and contract tests exist.

## Backend decisions

1. Extend the existing `AdapterCatalog` and `AdapterDescriptor`; no second registry. Retain typed provider interfaces (`ICurrentDataProvider`, `IHistoricalDataProvider`, `IAssetDiscoveryProvider`, etc.). Do not replace them with a string-and-JSON catch-all.
2. Add a versioned, stable `AdapterOperationDescriptor`: operation key, display name, capability kind, contract version, input/output schema IDs, data shape, units, timestamp/quality semantics, declared limits and read-only/effect classification. Adapter implementations own declarations; API maps them to a safe public DTO.
3. Distinguish static adapter capabilities from dynamic `ConnectionOperationAvailability` scoped to connection revision, asset, mapping, access, and provider compatibility. Missing/unauthorized/unmapped operations must fail closed; never silently select another provider.
4. Keep credentials, auth, session reuse, and provider calls in the existing integration gateway. The application API authorizes caller and connection scope; the gateway revalidates scope. Never expose secrets or unrestricted raw provider requests through metadata APIs.
5. Historical results require explicit series metadata, timestamp semantics, units, quality, source, truncation/continuation and aggregation, plus bounded windows/quantities. Preserve existing structured measurement schemas (e.g. paired load/position arrays) and payload limits. Adapt existing `EngineeringValue` conservatively; avoid a breaking rewrite.
6. Read-only preview, if added, is separate from metadata retrieval and must be permission-checked, rate-limited, bounded and auditable. Command support requires a distinct governed workflow and is out of scope.
7. Descriptor versioning: major changes require explicit compatibility handling; additive minor versions remain backwards-compatible. No speculative cadence or timeout figures should be displayed as verified.

## Delivery sequence

- **PR 1 — contracts**: inspect current descriptors, rules, provider interfaces and tests; add minimal operation metadata and catalog mapping; ensure discovery is excluded from public Operations projection; unit tests for versions, stable IDs and declared support.
- **PR 2 — implementations**: reconcile ForeSite trends and CygNet history POCs against `develop`; test quantity/window bounds, timestamps, quality, truncation, structured payloads and error paths with simulator fixtures.
- **PR 3 — API**: authorized adapter Operations endpoint and connection-aware availability; negative auth and no-fallback tests.
- **PR 4 — UI**: reusable two-panel Operations explorer, selected details, search, responsive behavior, accessibility and error states; no authoring UI.
- **PR 5 — hardening**: conformance fixtures, compatibility, gateway failure diagnostics, documentation and provider-specific validation.

## Validation gates

Each PR must run applicable .NET tests, frontend typecheck/build and integration checks. Verify CI before merging. Production ForeSite/CygNet access may not be available in CI; record this limitation and keep live validation separate. No POC branch is merged wholesale: inspect and selectively port compatible, tested changes.

## Decisions intentionally deferred

Module authoring; third-party adapter SDK/scaffolding; connection-specific live preview; command execution; site-defined quantity extension and cadence policy. These require separate decisions and acceptance criteria.

## Implementation checkpoint (2026-11-06, #485)

- Replaced inferred capability rows with explicit adapter-owned versioned operation descriptors. Unknown major operation versions are excluded. Current values retain existing read rules; structured surface cards include their payload fields, source units and array bounds.
- Added bounded typed history contracts extending `IHistoricalDataProvider`, with UTC `[start, end)` semantics, per-sample quality/units/source, raw aggregation, explicit truncation and safe diagnostics. The legacy collection-only history method refuses truncated results.
- ForeSite: selectively reconciled `poc/gap-g05b-time-range-trends`. PumpFillage history uses `CurrentPumpFillage`, the existing authenticated revision-scoped session pool and six-second gateway deadline. Source bodies are streamed with a 1 MiB ceiling, replies with a 256 KiB ceiling, windows with a 31-day ceiling and at most 1000 samples. Offset-free source times, fractional request bounds and malformed/duplicate fields fail closed. The source route retains the POC-verified whole-second timestamp format rather than guessing support for fractional route parameters. Source quality is `Unknown`; no quality is inferred from a successful HTTP response.
- CygNet: current reads remain unchanged. Added an optional typed historian seam and deterministic simulated history, with scope, filter, quantity, result and byte limits. Live CygNet has no history declaration and returns `CYGNET_HISTORY_UNSUPPORTED`. No fallback to current values or another provider occurs.
- Read-only APIs: `/connections/adapter-operations` and `/connections/{connectionId}/operations`, both requiring `connections.read`. Connection readiness names the exact saved adapter and revision, distinguishes disabled/incompatible/stale/failed tests, and expressly does not authorize individual assets or point mappings. These endpoints perform no provider value reads and return no settings or asset identities.
- UI: Connections → Adapters → Operations, using shared tabs, form fields, notices, search, selected-row styling, split-pane focus and scrolling. Overview, request/response/structured schemas and limits are descriptor-driven. Discovery stays in the unchanged import workflow. No command or preview execution was added.
- Reviewed `poc/gap-cygnet-history`, `poc/gap-cygnet-alarms` and `probe/poc-adapter-experience`; no branch was merged wholesale. Alarms, daily averages and unverified trend quantities are not advertised. PetroVisor remains discovery-only; WAMI is not installed and is not invented in the registry.

### Verification and external blocker

Local full solution builds, ForeSite recorded-shape tests, simulated CygNet and provider conformance tests, connection readiness tests, generated-client frontend typecheck/build/lint, frontend checks and interactions are recorded in PR #485. Local provider-to-Gateway HTTP tests also passed server/worker/anonymous authorization, provider selection, source scope, UTC/DST, quality, units, truncation and invalid bounds against simulated sources. CI includes the same history integration test, positive/negative metadata HTTP checks and eight responsive browser cases using synthetic descriptors; simulated tests are not live provider validation.

**Live-provider validation follow-up (owned by Sri locally, not a merge blocker):** production CygNet VHS history requires a licensed Windows CygNet host, the matching `CygNet.API.Historian`/`CygNet.Data.Historian` assemblies and validation of the native point ID, status, timeout and truncation semantics. The POC depends on a point-catalog branch absent from develop. An uncompiled native port was deliberately excluded from the deliverable rather than advertised as verified. Issue #484 records the local validation follow-up and selective production port. Live ForeSite validation also requires a reachable ForeSite service and Windows identity; it was not performed in this environment. Module authoring, live-value preview and command execution remain deferred as agreed.


## Phase 2 continuation

Native CygNet history work continues on `feature/adapter-provider-operations-phase2`, tracked by #484. The candidate
ports the POC behind the existing CygNet call gate and adds independently tested wire decoding/projection plus a
read-only licensed-host probe. It does not activate or advertise live history. See
[`cygnet-history-phase2.md`](../implementation/cygnet-history-phase2.md) for the exact host command, evidence requirements
and activation checks. Per Sri’s direction on 2026-11-07, native SDK/live-source validation is performed on his local Windows system and is not a merge requirement. No licensed SDK installation is expected in the hosted environment.

## Phase 3 checkpoint (2026-11-07)

Phase 3 was merged through #490 independently from PR #488's native CygNet candidate. Live CygNet history stays unsupported pending Sri’s local Windows validation. The candidate and test tooling may merge after hosted CI without installing licensed assemblies here. Phase 3 started from the verified `develop` baseline merged through #485.

- Added metadata-only `GET /api/v1/connections/{connectionId}/assets/{assetId}/operations?adapterKey=...&expectedRevision=...`. Both query parameters are required: the exact saved adapter and current revision must match, with no provider fallback.
- The endpoint requires fresh deployment `connections.read` and the existing SQL paired-grant `assets.read` predicate. Asset ownership and source bindings must belong to the explicit Connection. Missing, unauthorized, inactive and wrong-owner Assets share a 404. Permission and scope from different grants are never combined.
- Source selection is bounded to two matching bindings: none is `Unmapped`; multiple is `AmbiguousSource`. No first-match selection, source ID, raw snapshot, credential or Connection settings is exposed.
- Grant authority, Asset visibility, source bindings and Connection revision are rechecked after Gateway descriptor I/O. Revocation fails closed; changes to the pinned context return 409. This is a read-time check, not a reusable execution authorization token.
- The most positive eligibility state is `SourceCheckRequired`, never `Available`: a supported declaration, healthy Connection test and imported source cannot verify point mapping, device/quantity availability, live quality or values. Existing typed execution paths retain their separate authorization, Gateway session and read limits. No preview, value acquisition, command execution, discovery UI or authoring is added.
- API enum parity/serialization, pure state and service race tests, PostgreSQL paired-grant/ownership/source-bound tests, HTTP authorization/revision/no-fallback tests and OpenAPI string-enum checks cover the extension. The current frontend remains the metadata Operations explorer from #485; frontend validation uses regenerated clients without committing generated files.

Validation results and CI run links are recorded on the Phase 3 PR. Simulated tests and metadata checks are not live ForeSite/CygNet validation. Issue #484 remains open for licensed-provider validation and final hardening.
