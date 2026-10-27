# Authorized connection reads — implementation checklist

Baseline reviewed: develop b438c667 (#285 verified host, #286 pinned V3 execution,
#287 broker authentication), plans #284/#288 and POC 8161d97. POC code is reference
only; no merge/cherry-pick. Discovery/import/Views are existing and out of scope.

Implementation status after #289–#295 and the package-readiness slice:

| Gap | Implemented / remaining |
| --- | --- |
| B1–B3 / F1 | Existing pinned package and shared bounded host; regression coverage retained |
| B4 / G10 | #294 exclusive leases and conservative crash-orphan reconciliation; at-least-once execution remains |
| B5 / G1–G2 | #289 persisted scoped read plans; #290 authenticated RPC and saved-connection gateway routing |
| B6 / G3 | Strict parser and worker validation implemented; **blocked on owner evidence for live measurement timestamp/quality semantics** |
| B7 | #290 shared ForeSite session, bounded reads and serialized refresh; camel-case discovery filters preserved by regression test |
| B8 / G10 | #291 real package/NATS/Postgres end-to-end with simulated authorized engineering evidence and disabled-source failure; owner live acceptance remains |
| F4 | Nonempty ForeSite mapping rejected at configuration validation and dispatch; direct-well editor reuses existing validation |
| F6 / G4 | #291 bounded acquisition/module failure diagnostics persisted and displayed |
| F7 | 1.0.2 starters delegate freshness/quality to host policy; shared .NET validator and changed-policy cross-runtime regressions |
| F8 / G5 | #291 durable fair retry scheduling and poison-publication regression; publication gate/readiness added in final slice |
| F9 / G6 | #292 direct imported leaf well through existing configuration editor and source ownership checks |
| F10 / G7 | #293 existing binary upload/approval workflow; final slice adds exact artifact/publication/runtime readiness and retry facts |
| F11 | Existing If-Match/revision fences retained; source and active-revision changes tested |
| G8 | #291 existing diagnostic/history UI extended; #295 pinned package/connection-revision provenance |
| G9 | Separate Python audit complete: production worker is a scaffold; hosting remains unavailable and setup/dispatch refuse it |


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
- Slice B supplies the worker acquisition transport. There is no global URL fallback.
- Migration adds a nullable server-only column; no backfill and no credential
  migration. Apply through the existing migrator, never startup migration.

## Validation

SecurityPersistenceTests exercises two saved ForeSite connections sharing well 42,
opaque reference mismatch, source/connection/configuration changes, current grant
revocation, expiry, terminal requests, context restart, settings exclusion and
forged external ID/quantity/mapping/provider rejection. No live ForeSite required.
Worker contract tests retain simulator and pinned-package coverage.

## Local ForeSite handoff

Use [the owner acceptance checklist](foresite-local-shadow-checklist.md). It covers
Windows identity, saved connection and virtual directories, approved exact package,
direct-well/View setup, Shadow acquisition, persisted success/failure observations,
revocation, restart and replay. Live success is gated on owner timestamp/quality
evidence. Writes and autonomous control remain disabled.

## Slice B transport and conservative provenance

The worker sends only ExecutionId/ReadPlanId over authenticated NATS core RPC
(`wsa.rpc.current-measurement.v1`, outside durable event streams). The Server
resolves the persisted snapshot and calls the existing authenticated Gateway
adapter client. Settings cross only the existing Server-to-Gateway boundary;
responses contain only EngineeringValue or a typed failure code. Both broker
ends refuse a server that does not require authentication. Shared deployment
broker participants remain trusted, as in #287; subject ACLs are future work.

The Gateway's ServerPolicy-only ForeSite current-read endpoint uses the same
ForeSiteSession as discovery/test. Bounded connection/revision sessions serialize
operations, reuse cookies, retry one 401, preserve virtual-directory paths and
retire changed sessions without disposing active readers. Idle sessions expire;
capacity is bounded at 128. Gateway operations have six seconds within the
Server's eight-second budget and worker's ten-second request deadline.

**Local evidence gate:** POC-shaped CurrentPumpFillage/AveragePumpFillage and
LastGoodScanTime can be parsed, but the scan timestamp has not been demonstrated
to be the measurement time. Equal LastScanTime/LastGoodScanTime does not establish
Good quality. Such observations are explicitly Unknown/UnverifiedScan and cannot
pass worker validation, even when AllowUncertainQuality is enabled. Do not claim
live readiness or relax freshness to force a run. Owner-supplied sanitized payloads
and confirmed field semantics are needed to finish the live quality/time mapping.
Malformed, missing, nonnumeric, out-of-range or offset-free timestamps have typed
failures. The engineering contract adds optional retrieval time and timestamp-kind
metadata; old simulator fields and SDK module input constructors are unchanged.

Tests: ForeSiteAdapterTests reuses existing recorded-shape suite and adds mocked
connection/session/parser regressions. ExecutionDispatchTests adds a real broker
RPC check against fake authoritative storage/Gateway, proving worker routing fields
cannot override the persisted source and private error details do not escape.

## Slice D failure evidence and publication fairness

Failed V2/V3 executions retain at most 64 input diagnostics in the existing
execution OutputJson envelope (`inputDiagnostics`). The engine preserves the
original typed exception and collects values/UTC times/known quality/outcome
codes; it excludes provider response text, mapping JSON, settings and failed
module outputs. Persistence admits only observations matching a snapshotted
requirement/Asset/quantity/connection, bounds JSON at 32 KiB, and retains the
existing atomic terminal/Inbox/conflict-audit behavior. Old failure messages and
repository implementations keep their previous behavior. ExecutionDetailPanel
extends its existing observation table with acquisition reasons, retrieval time,
connection ID and an explicit unverified-scan warning; missing values render as
missing, not zero.

Package publications use durable bounded exponential retry scheduling (5 seconds
to 5 minutes), attempt count and safe failure code on the existing outbox rows.
A failed batch therefore releases later valid publications on the next poll.
CreatedAtUtc remains the original queue time; pending work is never discarded.
Restart preserves retry state. Successful delivery clears retry/error metadata.
Direct DispatchAsync remains an explicit recovery operation and retains approval,
retirement and row-lock fencing. A generated migration adds nullable scheduling/
error fields, default-zero attempt count and a selection index.

Acceptance added to ExecutionDispatchTests: a full batch of 21 poison publications
cannot starve a later approved archive. The existing real approved-package E2E
now also runs with persisted saved ForeSite source plans and actual authenticated
NATS RPC, once with synthetic verified engineering evidence and once with a
connection disabled after dispatch. Both success and bounded failure results are
persisted through the existing repository. This proves the execution boundary,
not live ForeSite timestamp/quality semantics; the HTTP parser remains separately
covered by mocked POC-shaped payloads and deliberately rejects unverified evidence.

Python parity audit: `src/workers/python/pyproject.toml` has no runtime dependencies;
`main.py` only handles stop signals and waits. It has no broker subscriber, V3
package host, authorized read client, lease/result publisher or package dependency
isolation. The authoring SDK/offline kit does not establish production hosting.
No Python execution capability is claimed or enabled by these .NET changes.

## Gateway failure classification

Authorized measurement calls reuse the existing Gateway client's transport
normalization. Timeouts and connectivity failures retain dependency codes;
caller cancellation propagates. Malformed/empty error responses become
`EXECUTION_READ_GATEWAY_FAILED`; malformed successful responses become
`EXECUTION_READ_GATEWAY_INVALID`. They must not be confused with
`EXECUTION_READ_PLAN_INVALID`, which is reserved for request/reference validation.
Valid bounded uppercase provider codes remain available for diagnostics, while
upstream body text and service caller credentials stay outside worker replies.
API contract regressions cover HTML/login responses, absent bodies, typed errors,
invalid codes, malformed success, transport failure, timeout and cancellation.

## Package delivery and runtime readiness

The existing catalog now reports stored archive, recorded publication, exact
authenticated object-store availability, durable retry facts and observed
Gateway/worker health. The worker readiness check requires broker authentication
and reports its actual module SDK version. The same compatibility validator used
by package execution evaluates the reviewed manifest against that worker.
Older plain-text health responses remain readable but do not prove SDK compatibility.

Hosted V3 dispatch refuses an approved stored package until its publication audit
is committed. This closes the approval-to-delivery race; metadata-only V2
compatibility remains. Observed readiness grants no source authority and cannot
guarantee future availability. Actual execution still verifies hash/manifest and
current saved-connection permissions. The panel uses the existing catalog,
outbox, approval controls and generated client; there is no second package store.

Remaining work: owner live evidence/acceptance and
a separate service-account design before scheduled live reads. Python production
parity requires a real host/dependency-isolation design and is not enabled here.
