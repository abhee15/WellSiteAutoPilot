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
