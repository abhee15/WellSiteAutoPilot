# Authorized connection reads — implementation checklist

Baseline reviewed: develop b438c667 (#285 verified host, #286 pinned V3 execution,
#287 broker authentication), plans #284/#288 and POC 8161d97. POC code is reference
only; no merge/cherry-pick. Discovery/import/Views are existing and out of scope.

| Gap | Current implementation / owner | Remaining acceptance |
| --- | --- | --- |
| B1–B3 / F1 | Already implemented: pinned package and shared bounded host | Retain regression tests |
| B4 / G10 | Partial: collectible per-run host cleans normal exits | Crash-orphan cleanup, restart lifecycle |
| B5 / G1–G2 | Slice A: ExecutionRepository pins source/connection revision and opaque read ID in V3; server-only authority persisted atomically | Slice B transport to Gateway, two-server HTTP isolation |
| B6 / G3 | Needs local verification: LastScanTime/LastGoodScanTime semantics | Strict mocked parsing and owner fixtures; never infer Good from equal scan times |
| B7 | Missing current reads; existing ForeSiteAdapter owns cookie/token/error behavior | Reuse session, total read budget, 401 concurrency tests |
| B8 / G10 | Existing package/NATS/Postgres test; no ForeSite substitute yet | Simulated complete Shadow acquisition, zero writes |
| F4 | Slice A rejects nonempty ForeSite provider mappings at dispatch | Configuration validation/UI feedback |
| F6 / G4 | Missing: failed input observations discarded by engine | Bounded failure diagnostics through existing result history |
| F7 | Partial: worker validates manifest-derived input policy; starters duplicate 60 seconds | Align both starter languages with reviewed policy |
| F8 / G5 | Missing: publisher selects oldest 20 repeatedly | Fair durable retries and poison-publication regression |
| F9 / G6 | Partial: existing setup supports asset bindings, required adapter forces View | Extend existing selector/backend ownership checks for direct wells |
| F10 / G7 | Partial: upload/approval API exists; catalog UI lacks upload/readiness flow | Extend catalog, API clients and existing controls |
| F11 | Existing UI uses If-Match | Documentation only |
| G8 | Partial: ExecutionDetailPanel/history exist | Connection, package and bounded observation/error display |
| G9 | Python authoring kit exists; production hosting not established | Separate parity audit before implementation |

## Slice A policy and compatibility

- The execution row is the durable read-plan store. A random ReadPlanId selects
  exactly one snapshotted input; modules and workers receive no settings/principal.
  No independent connection registry or Gateway-local preparation state.
- Server-only ReadAuthorityJson retains authenticated user and bounded Windows
  proof. Each acquisition uses existing paired-grant queries/fences, the current
  active configuration, current Asset owner/SourceBinding and enabled connection
  at the pinned revision. Revocation, revision/source change, completion or expiry
  refuses subsequent acquisitions. Already authorized in-flight HTTP reads may
  complete; no distributed atomic cancellation is claimed.
- Direct-user read authority expires after five minutes. Windows group proof
  retains its original one-minute lifetime; persistence never refreshes it. A
  delayed request needs a fresh authenticated Shadow request.
- Scheduled live reads require an explicit service-account authorization design;
  they fail closed when no trusted subject exists. Existing simulator scheduling
  remains supported. This is not a fabricated user/group identity.
- V3 input Source metadata is additive. V1/V2 simulator behavior is retained.
  Connection-backed Assets cannot opt into the simulator path. Unpinned packages,
  unsupported providers/quantities/mappings are rejected for live reads.
- Slice A deliberately leaves live input acquisition unavailable at the worker
  until the following Gateway transport slice. There is no global URL fallback.
- Migration adds a nullable server-only column; no backfill and no credential
  migration. Apply through the existing migrator, never startup migration.

## Validation

SecurityPersistenceTests exercises two saved ForeSite connections sharing well 42,
opaque reference mismatch, source/connection/configuration changes, current grant
revocation, expiry, terminal requests, context restart, settings exclusion and
forged external ID/quantity/mapping/provider rejection. No live ForeSite required.
Worker contract tests retain simulator and pinned-package coverage.

## Local ForeSite handoff (pending subsequent slices)

Do not use slice A alone for live testing. Final checklist will include Windows
identity, saved connection with virtual-directory URL, approved exact package,
direct-well/View setup, Shadow acquisition and persisted bounded success/failure
observations. Timestamp/quality compatibility remains owner verified. Writes and
autonomous control remain disabled.
