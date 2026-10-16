# Configured Shadow request and result workflow — slice C

The Logic workspace can request a manual Shadow run of the reviewed Active
revision, recover an ambiguous request, and inspect persisted progress, results,
input quality and source/timestamp observations. It uses the same V2 snapshot
builder as scheduled dispatch. The browser sends only a request key; module,
parameters, exact Assets, data requirements and provider bindings come from the
immutable persisted revision. There is no physical-control capability in this UI.

## Request contract and authority

`POST /api/v1/configured-logic/{id}/revisions/{revisionId}/shadow` accepts
`{ "requestId": "<nonempty GUID>" }` and returns a minimal canonical `ConfiguredShadowReceiptResponse`
with HTTP 200 for both first submission and recovery. Receipts contain identity/mode/contract metadata only, never output, result or failure details; those require the separate scoped execution-read policy. It requires the existing
`executions.request` policy plus the existing exact Asset-scope check over the
configuration. Execution readback independently requires `executions.read` and
its persisted Asset-scope snapshot. Capability metadata adds `canRequestShadow`
from live legacy permission claims; it confers no authority. Grant cutover (#76),
trusted publisher/package provisioning (#87) and Windows IWA release validation
(#92) remain separate. CI trusted catalog fixtures are not a production trust path.

The service validates Active/Shadow status, installed enabled module identity and
active Asset bindings. During commit, a configuration-row lock rechecks the active
pointer, revision, module enablement and Asset availability. Old reviewed revisions
cannot create a new execution after activation changes. Replays of an already
committed request return that execution even if its revision was later superseded,
subject to current endpoint authorization. Reusing a key for another configuration
or revision returns `EXECUTION_REQUEST_KEY_CONFLICT` (409).

## Durable request identity and atomicity

Execution IDs for this endpoint derive from the first 16 bytes of SHA-256 over the
UTF-8 string `wsa:manual-shadow:v1:{userId:D}:{requestId:D}` using canonical .NET GUID
formatting, then .NET's Guid(byte[]) representation. This versioned algorithm is a
persisted contract and must not change silently. It binds a request key to one user
and execution without adding a second receipt table or changing legacy request IDs.
Execution rows must be retained as idempotency receipts; any future retention/purge
feature must preserve these receipts before deleting execution history.

PostgreSQL transaction advisory locks use separate request/configuration/started-
message namespaces. Hash collisions only serialize unrelated work. The request
lock orders concurrent retries; the configuration lock orders manual and scheduled
V2 overlap checks. The configuration row lock orders dispatch against activation.
Execution, exact scope, V2 outbox and authenticated actor/correlation audit commit
in one transaction. Audit excludes parameters and provider mappings. Pending/running
overlap for the same Configured Logic and exact Assets returns
`EXECUTION_ASSET_OVERLAP` (409); this is not a global cross-configuration lease.
No persistence migration or installer/topology change is needed.

The browser saves a per-user/configuration/revision key in sessionStorage before
POST. It blocks submission when storage or secure randomness is unavailable or a
saved key is corrupted. It never automatically repeats a write. Explicit recovery
uses the same key and can submit once if the original request never committed.
Keys survive navigation/reload in that browser tab. Clearing storage or switching
tabs is not recovery; inspect execution activity before requesting another run.
A fresh request requires separate review after observing a terminal result.

## Progress and input provenance

The worker emits additive `wsa.execution.started.v2` before running a Shadow module.
The existing execution stream already captures this subject. The V2 result consumer
applies Running + StartedAtUtc with Inbox deduplication in one transaction. A late
or duplicate started observation cannot overwrite a terminal result or its timestamps.
Existing V1 requests/results and V2 completed/failed contracts remain compatible.

Completed V2 output retains existing result fields and adds `inputObservations`
with requirement/Asset identity, quantity, value, unit, UTC timestamp, quality and
source. It excludes provider credentials, mapping objects and parameters. Input
checks fail closed for unsupported quality; unit/quantity mismatch, future/stale
telemetry and unsupported access have stable failure codes. Failures remain
inspectable through the existing persisted failure code/correlation fields; raw
provider errors are not reflected by request guidance.

The scoped execution detail panel polls reads only while persisted state is pending
or running, stops at terminal state or error, and clears data on access loss. The
observation table is capped at 100 rows and accepts only bounded typed fields from
at most 1 MiB of JSON. Text is escaped, numbers use locale formatting without the
default three-decimal rounding, and timestamps show the display timezone. The
existing bounded raw-output preview remains available. UI controls are localized,
keyboard accessible and use semantic tokens with responsive layout.

This request deduplication does not promise exactly-once worker execution after
broker/worker failures. Existing deterministic JetStream result IDs, atomic terminal
winner selection/conflict audit, and governed Shadow result materialization remain
in force; worker fencing/replica isolation is separate hardening. No command is
replayed or executed physically by this workflow.

## Verification

React DOM tests cover review, saved-key recovery after unmount/reload, no automatic
retry, storage/corruption/randomness handling, denied/mismatched readback, pending
polling, terminal reset and escaped provenance. Real HTTP/PostgreSQL tests cover
concurrent same-key requests, scope/permission denial, stale revisions, overlap,
singular outbox/audit, durable Running and late-started terminal preservation.
The full runtime CI test uses an approved built-in module fixture, real PostgreSQL,
JetStream, worker, Gateway and offline simulator, verifying successful V2 output,
request recovery after revision drift and durable provider failure. All seven jobs
remain merge gates on the exact head.
