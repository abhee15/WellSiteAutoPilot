# ForeSite native applicable-quantities read — incremental implementation

**Scope:** A read-only, bounded retrieval of ForeSite's native `WellQuantity` enum IDs for an explicitly selected well. This is a prerequisite for an authoring-ready quantity catalog, **not yet** the catalog itself.

## Provider evidence

- POC source: `docs/provider-logic-module-gaps/docs/implementation/provider-logic-module-gaps.md` and `foresite-adapter-reference.md`.
- Recorded response: `docs/implementation/fixtures/foresite/Surveillance_AvailableQuantities_776.json` on the POC docs branch (32 integers).
- Provider endpoint: `GET Surveillance/AvailableQuantities/{wellId}`.
- Response is **numeric enum IDs only**, without names or units; it is distinct from `Surveillance/well/{id}`, whose `Value` and `Units` blocks contain readings and unit metadata.
- An ID's numeric value must not be guessed from a field name or inferred from its position in a recorded response.
- The recorded POC corpus does not yet contain a verified source `WellQuantity` enum-to-ID table; do not present these numeric IDs as author-facing keys.

## What this slice implements

`ForeSiteApplicableQuantities.ReadAsync(ForeSiteSession, externalId, cancellationToken)` uses the existing ForeSite cookie/Windows-auth session and provides:

1. Positive numeric well ID validation before I/O.
2. Read-only authenticated API request, preserving the configured base-path and one 401 token refresh.
3. Bounded response handling: 16 KiB maximum, including unknown-length/chunked streams, 256 maximum native enum IDs.
4. Strict top-level numeric array, positive 32-bit IDs <= 65535, duplicate rejection, safe structured failures, cancellation propagation.
5. An immutable set of native IDs with a membership check; no mutation of the provider or manufactured named quantities.
6. Offline tests using the recorded well 776 response and negative/authentication/limit/cancellation fixtures.

The method is **not wired into the hosted module read path or advertised as an executable catalog operation**. This is intentional: a false `CurrentPumpFillage -> ID` assumption could incorrectly deny a valid measurement or authorize an inapplicable one. The existing current-value reader and allowlist remain unchanged.

## Next prerequisite gates before runtime integration

- Verify exact `WellQuantity` enum ID to provider quantity name mappings against the ForeSite interface/source or a controlled live system, including well/device-specific availability (e.g. RPOC vs AEBEAM).
- Reconcile the verified enum IDs with the existing safe platform quantity keys and the POC `ForeSiteQuantityCatalog` value/unit field mappings.
- Build the typed **applicable / available / valid / fresh** catalog using the provider's applicability response. Status fields alone must not determine applicability.
- Bind the catalog to an authorized, pinned connection revision and asset identity through the Gateway. Decide caching/invalidation on connection revision, asset configuration and discovery refresh; do not cache across unrelated security contexts.
- Acquire supported current values from one status response for a run where feasible. Validate source units, sentinels, per-field meaning, common `LastGoodScanTime`, retrieval timestamp, and declared maximum age without inventing sensor quality.
- Add source-shaped tests for applicable-but-unpopulated, unsupported, duplicate IDs, zero-valued measurements, stale scans, missing units and changed configuration.
- Only after those tests and runtime binding are complete should the existing reference guide's `ReferenceOnly` state become `Implemented`.

## Explicit exclusions

No new module manifest schema; no arbitrary provider-native enum access from authored modules; no direct client credentials; no new Gateway web endpoint or browser read; no new command/writes. Live customer confirmation must remain a separate acceptance gate.

## Shared surveillance snapshot increment

The two **already-reviewed** current scalar operations (`PumpFillage`, `AveragePumpFillage`) now share one ForeSite status parser. An internal `ForeSiteSurveillanceSnapshot.ReadAsync` can acquire both together from **one** `GET Surveillance/well/{wellId}` response with one `LastGoodScanTime` and one `RetrievedAtUtc`. It validates every requested quantity before I/O, refuses duplicate/unsupported/oversized requests and fails the whole snapshot if a required value or timestamp is invalid. The prior scalar `ForeSiteMeasurements.ReadAsync` delegates to the same parser and retains its public contract.

`ForeSiteConnectionMeasurements.ReadSnapshotAsync` keeps the existing saved connection/revision session, authentication, per-session read gate, six-second budget and Gateway failure mapping. There is **no cross-execution cache**: separate executions cannot accidentally inherit each other's acquisition time or asset context.

This is a foundational **internal method only**. It is not a new hosted module binding, not a dynamic-quantity allowlist, and not proof that ForeSite's `AvailableQuantities` enum IDs have been verified. The static UI workflow must stay `ReferenceOnly`. Readiness requires a verified native enum map, applicable/available/valid/fresh authoring contract, explicit authorized source bindings, and a reviewed execution/read plan before module use. The controller-scan `Good` quality remains an acquisition indication, **not** independent sensor validation or proof that the scan age satisfies a module's maximum age.

Deterministic CI tests prove a single well-status fetch for two values, zero-value preservation, shared acquisition and retrieval timestamps, connection revision isolation, failure on partial/bad results, no HTTP writes, and cancellation. Customer ForeSite acceptance remains pending.
