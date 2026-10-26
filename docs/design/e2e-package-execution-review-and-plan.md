# Live package execution: review and next implementation plan

Review date: 26 October 2026.

## Implementation status after review

The findings below describe the reviewed POC, not the current implementation
status. Develop now includes the verified host and authenticated V3 package path
through merge commit `b438c667a34e5f27b1ebde9c346a307e72129def`. Follow-up work
is split into cohesive pull requests:

| Follow-up | Status | What it establishes |
| --- | --- | --- |
| [#285](https://github.com/abhee15/WellSiteAutoPilot/pull/285) | Merged; all nine checks passed | Asynchronous exact-hash .NET host, shared archive/SDK preflight, bounded download, per-run instance and collectible dependency context, managed dependency streams and cleanup. Production registration stayed unchanged in this foundation. |
| [#286](https://github.com/abhee15/WellSiteAutoPilot/pull/286) | Merged; all ten checks passed on the combined authenticated head | V3 pins approved archive hash and reviewed manifest atomically with dispatch, explicit V2 built-ins, hosted production execution, scope/result version parity, and real PostgreSQL/NATS/package/worker integration. Accepted runs retain their pinned package after retirement; retirement blocks new requests. |
| [#287](https://github.com/abhee15/WellSiteAutoPilot/pull/287) | Merged prerequisite; all ten checks passed | Independent protected broker token, authenticated installed/dev clients, anonymous/wrong-token rejection, and package-source refusal when the broker does not require authentication. Merged before hosted execution was enabled. |

The merged host uses verified temporary extraction per execution, with no
persistent cache. That deliberately avoids a cache integrity/eviction contract
until measured load justifies one. Download is bounded at 16 MiB and 30 seconds;
shared archive preflight also bounds extraction. Managed dependencies load from
streams so Windows cleanup can succeed. Native package dependencies are rejected.
This is hosting for trusted approved modules, without hard termination or an
arbitrary-code sandbox. Normal completion and caught failures clean extraction;
a process crash can leave working directories behind. Startup reconciliation of
those directories, publication-versus-worker readiness and bounded resource/process
isolation remain operational follow-ups. A missing delivered object produces a
controlled failure; this first path does not promise automatic execution retry.

The production package path landed only after #287 and the combined #286 head
passed CI, including installed Windows services. Broker authentication was an
additional integration finding:
package hashes alone cannot establish dispatch authority when an anonymous local
publisher can forge the expected hash and manifest. All runtime broker accounts
remain trusted participants sharing a deployment token; per-service subject ACLs
and untrusted customer code are separate future designs.

Next implement slices 3 and 4 below. The POC supplies a useful endpoint/field
starting point; its global URL and freshness workaround are not production
routing or measurement policy. Slices 5–8 remain future work. No live ForeSite
connection, provider write, or physical-control path has been enabled here.

## Scope and evidence

Reviewed `poc/e2e-package-execution` at
`8161d9739e7915a049c973d5d1cbb4c187accb0e`, against merge base
`88304bdfcb7577ed28762fd9b18b88e7d8061029` and develop
`b1b1d974c7062dbea1209e80b6092987b52010a9`. The branch became available during
the review. It contains one commit, four production files, 191 added lines and
two removed lines. It is two develop commits behind; those two changes concern
web sign-in polling and list styling. No tests, manifests, installer changes or
deployment recipe are included in the feature diff.

The supplied screenshot is separate owner-run evidence: an approved package
reportedly ran against live ForeSite, observed 99.6% PumpFillage and completed in
Shadow. It also records a scratch-only four-hour freshness limit and unexplained
LastScanTime behavior. That temporary module version is not in this branch.
The Windows checkout and scratch runtime are not accessible here, so live
behavior and the startup issue were not reproduced.

Assessment: retain this as a useful POC; do not merge its default production
registration unchanged. It proves where hosting and real reads connect, while
the findings below identify the required integration work.

## Assessment

Keep the architecture already built: Server-owned catalog and approval,
immutable database package bytes, durable NATS distribution, runtime-owned
provider reads through the Gateway, and provider-neutral modules. The next work
should complete those boundaries rather than introduce another module registry,
provider client inside modules, or independently deployed service.

The most important integration gap is that a configured local Asset is not yet
bound all the way through execution to its saved connection and source identity.
The most important hosting gap is that execution requests do not pin package
bytes. The most important live-data uncertainty is the meaning of ForeSite's
timestamp and quality. A successful sample does not resolve these three issues.

## Foundations worth preserving

- Upload stores unchanged archive bytes with disabled/untrusted registration in
  an authorized atomic transaction. Same ID/version cannot acquire new bytes.
- Approval, audit and publication request commit together. Publication rechecks
  the reviewed catalog manifest/hash and fences retirement with row locks.
- The shared package reader already rejects unsafe archives and checks expected
  identity, runtime and SDK compatibility. Extend it; do not duplicate parsing.
- Source bindings already identify `(ConnectionId, ResourceKind, ExternalId)`
  and local AssetId. Imported Asset names are never routing identity.
- Configured Logic has immutable revisions, frozen View selections, paired grant
  checks and atomic activation/manual-request concurrency boundaries.
- The worker validates quantity, units, age and quality before executing. Explicit
  module failure prevents outputs from escaping as successful results.
- Execution result persistence already records conflicting terminal results in
  audit. Do not reopen the historical claim that this conflict is invisible.

## Feature-branch findings

These findings are from the actual four-file diff. Source links are pinned to
the reviewed commit. P1 items should be resolved before enabling the new resolver
and provider as the general installed path. Data semantics requiring live proof
are explicitly distinguished from defects established by code inspection.

| ID | Priority | Finding / concrete consequence | Required change and regression |
| --- | --- | --- | --- |
| B1 | P1 | [Resolver lines 40–51](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/workers/WellSiteAutoPilot.Worker.DotNet/Execution/PackageLogicModuleResolver.cs#L40) list and download Object Store entries until a matching manifest appears. The hash is compared only to the object's own name, not an authoritative execution hash. A malformed unrelated manifest can throw and prevent reaching the requested package; cold resolution also downloads unrelated archives. Retired artifacts intentionally remain in this store. | Pin the approved archive hash in the request and retrieve exactly that object. Check catalog identity/manifest and loaded module identity. Test a valid target alongside corrupt/unrelated objects and a conflicting identity/hash. Never treat membership in the store as current approval. |
| B2 | P1 | [TryLoad lines 57–88](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/workers/WellSiteAutoPilot.Worker.DotNet/Execution/PackageLogicModuleResolver.cs#L57) uses independent ZIP/JSON parsing and never enforces minimumSdkVersion. It extracts before shared structural/host preflight, with no download/expanded-byte limit. Case-insensitive version matching also differs from the existing exact package-identity rule. | Reuse LogicModulePackageReader and LogicModulePackageCompatibility before extraction or constructor execution. Test too-new SDK, altered manifest/identity, archive limits and unsafe entries with the module entry point never reached. Use canonical path containment including a directory separator; a string prefix alone is not the extraction policy. |
| B3 | P1 | [Resolve lines 25–36](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/workers/WellSiteAutoPilot.Worker.DotNet/Execution/PackageLogicModuleResolver.cs#L25) blocks on async network I/O inside a single resolver-wide lock. No cancellation token or download deadline reaches NATS. A slow first load blocks unrelated module resolution, and shutdown/lease cancellation cannot interrupt this stage. | Make resolution asynchronous, bounded and cancellable. Coordinate preparation per package hash rather than with a global lock. Test a stalled object read, worker shutdown and simultaneous resolution of different hashes. |
| B4 | P1 | [Extraction/loading lines 71–102](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/workers/WellSiteAutoPilot.Worker.DotNet/Execution/PackageLogicModuleResolver.cs#L71) creates a random temp directory, never cleans it up, uses a noncollectible load context and caches one module instance by ID/version for process lifetime. There is no persistent hash cache, eviction or defined instance/thread-safety contract. Repeated restarts accumulate directories; versions remain loaded until restart. | Define a host-owned cache root/ACL, atomic verified entries, dependency lifetime and cleanup, with explicit per-run module instance semantics. Prove restart recovery and dependency/version separation. Choose process isolation if hard termination is required; load contexts do not provide that. |
| B5 | P1 | [Gateway registration lines 78–93](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/integration/WellSiteAutoPilot.IntegrationGateway/Program.cs#L78) registers ForeSite for all reads using one global URL, defaulting silently to localhost. Saved connection settings and SourceBinding are not used. The URI is not normalized like ForeSiteAdapter.BaseAddress, so a configured path prefix without a trailing slash can be replaced by relative read paths. | Route via a reviewed saved connection/source plan. Missing settings must produce a clear unavailable result; do not fall back to localhost. Test two servers with identical numeric well IDs and a base URL with a virtual-directory prefix. |
| B6 | P1 live evidence gate | [Provider lines 37–45](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/integration/WellSiteAutoPilot.IntegrationGateway/Providers/ForeSiteCurrentDataProvider.cs#L37) assumes Value.CurrentPumpFillage/LastScanTime and labels quality Good when LastGoodScanTime equals LastScanTime. Code confirms this heuristic, but not its ForeSite meaning. The screenshot's moving scan time makes it possible that accepted age describes retrieval rather than measurement. | Obtain sanitized recorded responses and the field semantics. Test null/missing/non-numeric values, stale measurement with fresh retrieval, timestamp offset/timezone and known bad quality. Do not claim the quality heuristic is established provider truth. |
| B7 | P1 integration | [Provider lines 50–70](https://github.com/abhee15/WellSiteAutoPilot/blob/8161d9739e7915a049c973d5d1cbb4c187accb0e/src/integration/WellSiteAutoPilot.IntegrationGateway/Providers/ForeSiteCurrentDataProvider.cs#L50) duplicates the existing ForeSite session/error path. EnsureSuccessStatusCode drops FullErrorMessage. Missing/wrong JSON fields produce general exceptions. Concurrent 401s each force another token creation after acquiring the gate; the gate serializes refreshes but does not coalesce them. The new upstream timeout is 30 seconds while GatewayDataClient times out at 10 seconds. | Extract/reuse a connection-scoped ForeSite session with one-401 retry, JSON/error handling and an agreed total read budget. Test cookie reuse, simultaneous expiry, refresh failure/recovery, full error messages, malformed DTOs and cancellation. |
| B8 | P1 acceptance | The production DI registrations switch immediately to the package resolver and add the live provider, but the feature contains no new tests or reproducible setup instructions. No PR-triggered workflow runs were returned for the reviewed SHA. Existing publication tests do not execute this loader. | Add focused loader/provider tests and a real package-through-worker CI test before promotion. Record exact package/version/hash and cold-start recipe, with zero provider writes. Check current-head CI rather than assuming the owner's successful run covers deployment behavior. |

The new provider's two-quantity allowlist and preservation of Windows credentials
are useful starting points. Its singleton HttpClient also retains a pooled
cookie-bearing handler indefinitely; session/client lifetime should be designed
per saved connection when B5/B7 are addressed. ForeSite still advertises only
AssetDiscovery in its adapter descriptor, so capabilities/test status and actual
current-read support must be brought into agreement.

## Findings and priorities

The following baseline findings explain why fixes span more than the four POC
files. B1–B8 above anchor the direct feature review.

| ID | Priority | Finding and consequence | Code evidence / next action |
| --- | --- | --- | --- |
| F1 | P0 before general live execution | Requests pin module ID/version but no catalog package hash/runtime/SDK expectation. The resolver takes only ID/version. An object available in NATS is not execution authority. | `ExecutionMessages.cs`, `ExecutionRepository.StageConfiguredRequest`, `ILogicModuleResolver`. Snapshot authoritative package identity at authorized dispatch and verify those exact bytes before loading. |
| F2 | P0 before multiple live connections | Worker reads send provider ID, external Asset ID and quantity. No ConnectionId, source-binding identity or saved connection revision reaches the Gateway. A global ForeSite URL cannot distinguish two saved ForeSite connections. | `GatewayDataClient`, Gateway `Program.cs`, `SourceBindingEntity`. Carry an authorized connection/source read plan; use saved settings rather than a user-entered URL or display name. |
| F3 | P0 live evidence gate | Screenshot LastScanTime advances with retrieval time, and a four-hour limit was used to get past a stale-input failure. Neither proves measurement freshness or Good quality. | Baseline ForeSite adapter advertises discovery only. Verify the POC's field extraction, units, value timestamp, timezone, quality mapping and missing/error behavior against sanitized live responses. |
| F4 | P1 with F2 | ProviderMappingJson is validated and snapshotted but not used by `ModuleExecutionEngine.ReadInputAsync` or `GatewayDataClient`. Syntactically valid configuration can imply a mapping the runtime ignores. | Give supported measurement mappings an explicit versioned adapter contract. Reject unsupported nonempty mappings; avoid presenting editable fields with no runtime effect. |
| F5 | P1 with loader | .NET resolves only the built-in sample; Python production main only waits for stop. Loading reviewed code introduces dependency, timeout, cancellation, output-size and filesystem lifecycle requirements. | `BuiltInLogicModuleResolver`, worker startup, Python `main.py`. Review POC extraction/load/dependency behavior and introduce bounded hosting. An AssemblyLoadContext is dependency separation, not an arbitrary-code sandbox. |
| F6 | P1 | Failed reads and failed module evaluations lose input observations: the engine throws, and ExecutionFailedV2 carries only timestamps/code/attention. | `ModuleExecutionEngine`, `ExecutionRequestV2Consumer`, `ExecutionFailedV2`. Persist bounded input diagnostics on failure, without admitting failed module outputs. |
| F7 | P1 | Freshness policy is duplicated: both starter languages hardcode 60 seconds, and the manifest also specifies 60. Changing only the manifest does not update the algorithm checks. | `modules/authoring/dotnet/WellInputCheck/EntryPoint.cs`, Python starter, both manifests. Establish one reviewed policy and test offline/live parity; do not ship the scratch four-hour value as a default. |
| F8 | P1 reliability | Publication always selects the oldest 20 pending rows. Failures remain pending with no scheduling change. If those 20 fail persistently, later valid packages never enter a batch. | `ModulePackagePublisher.DispatchBatchAsync`. Add bounded retry scheduling or another fair selection rule, with durable visible errors and a recovery action. Preserve pending work and existing retirement locks. |
| F9 | P2 after F2 | RequiredAdapterKey requires a View in backend ResolveViewAsync and disables UI Save without one. A one-well module therefore requires a one-well View. | Permit an explicitly reviewed direct Asset selection from one compatible connection using the same ownership/source checks. Preserve Views for reusable target sets. |
| F10 | P2 | Package upload exists on the Server, but the catalog UI has no upload flow. Approved metadata does not establish delivery or compatible worker availability. | Reuse raw upload endpoint and existing approval controls; show trust, publication and runtime compatibility separately. Never turn upload into automatic approval. |
| F11 | Documentation / tooling | `If-Match: "none"` is the first-activation concurrency token, not a bypass to remove. The web workspace already sends it and later sends the reviewed active revision ID. | `ConfiguredLogicWorkspace.transition`, `ConfiguredLogicService.ActivateRevisionCoreAsync`. Correct sample scripts/docs and retain conflict behavior. |

P0 means a gate on treating the POC as generally deployable live execution. It
does not prevent continued bounded Shadow investigation with explicit test setup.

## Proposed execution contract

At request creation, the Server resolves and snapshots:

1. Exact catalog version, package SHA-256, runtime and required SDK version from
   approved/enabled metadata. Metadata-only built-ins are an explicit case, not
   a fallback when an uploaded package is missing or corrupt.
2. Exact reviewed configuration revision and frozen local Asset role bindings.
3. For each input, the saved source binding and owner ConnectionId, adapter key,
   connection configuration revision, quantity/mapping version and input policy.
4. Correlation identity and the validity/authorization policy for queued work.

Keep provider credentials out of modules and execution messages. Keep database
access in the Server. Do not let the worker read business tables or reach Server
administration APIs. Use the shared package preflight against the expected
identity supplied by the Server; never derive expectations from the archive.

There is one routing decision to close before F2 implementation: how the Gateway
receives trusted saved settings for a particular connection revision. Prefer
reusing the existing authenticated Server-to-Gateway boundary to install a
bounded execution read plan, referenced by the worker using execution ID plus
plan identity. The worker must not gain the Server's adapter/settings rights.
The design must specify plan persistence/recovery on Gateway restart, expiry,
idempotent preparation, disablement and source-change invalidation before this
option is accepted. Compare that small plan with a versioned connection-settings
projection; choose the simpler complete option, not both. Do not silently solve
this by accepting arbitrary base URLs on the worker read endpoint.

Define queue-time semantics explicitly: retirement or connection disablement
after enqueue must have a documented start boundary. A checksum cache cannot
answer whether a run is still authorized. Revalidate live eligibility through
the selected read-plan/control mechanism; test changes before enqueue, while
queued and during execution. Do not promise instant cancellation of a run
already executing unless that mechanism exists.

Use a new execution message version for incompatible required fields. Keep the
existing V1/V2 behavior explicit per the contract-versioning rules; newly hosted
packages must not run from older messages lacking identity. Avoid compatibility
machinery beyond currently supported development contracts.

## Hosting and data decisions

### Package resolution and bounded execution

Download by hash into a temporary location, enforce transfer/expanded-size
bounds and reuse shared preflight. The first host verifies every execution and
cleans up its own extraction; no persistent cache is required. If measured load
justifies caching, publish complete entries atomically, coordinate preparation
per hash and define cache integrity/ACL, recovery and eviction behavior. A
hash-shaped directory name alone is not verification. Test worker restart, two versions of one
module, private dependencies, missing entry points and incompatible SDKs.

Use asynchronous resolution and explicit execution lifetime ownership so that
load contexts, temporary files and dependencies can be released. Pin the host
SDK assembly identity. Do not let package payloads substitute host libraries.
Set execution deadline and result-size limits. A token alone cannot stop a
noncooperative in-process module; use a child process where hard termination is
required. Weatherford-approved packages are the first hosting scope. Customer
code needs a separately demonstrated isolation model.

Python should reuse package identity, input planning and result semantics after
the .NET path is stable. Use a specified JSON subprocess protocol, supported
interpreter/dependency layout, bounded stdout/stderr, deadline and process-tree
termination. No network dependency installation in an on-premises runtime.
PowerShell remains future work; do not add an enum before its host exists.

### Measurement provenance and failure evidence

Keep measurement time distinct from retrieval time. Do not use UtcNow to turn
an unknown measurement time into a fresh value. Preserve the original source
field and timezone interpretation in diagnostic evidence. If ForeSite lacks a
meaningful measurement timestamp or quality, represent that uncertainty and
reject a requirement that needs evidence the source cannot provide.

Record selected connection/source, requirement, unit, source time, retrieval
time, effective age limit and rejection reason for each attempted input.
Include successful reads preceding another input failure. Bound diagnostic
payload size and use existing execution read authorization. Exclude credentials,
raw vendor response bodies and failed module recommendations/intents/state.
Separate acquisition, validation and module-evaluation failures in the UI.

Use a documented evaluation instant near actual evaluation, with deterministic
tests for queue delay and slow reads. Current code compares age to the consumer's
start time; that can understate age after slow acquisition. Keep allowed clock
skew separate from maximum input age. If algorithm-specific stricter checks
remain, document them and keep both starter languages in parity.

### Target selection and usability

Use SourceBinding to prefill well identity and resolve supported measurements
from the selected adapter. Asset discovery and measurement discovery are
different capabilities; prefer a small verified descriptor for PumpFillage
before building a generic vendor-field browser. Show unsupported requirements
before activation. Do not label every ForeSite quantity as supported because
one quantity worked.

Support direct one-well selection first. Do not turn a one-well algorithm into
a batch algorithm by increasing maximumCount. Applying it across a View needs
an explicit later per-well fan-out design with per-well execution IDs, overlap,
authorization, concurrency and partial-failure behavior. Frozen selections and
review of membership/source changes remain authoritative.

## Implementation sequence and acceptance

| Slice | Deliverable | Required evidence |
| --- | --- | --- |
| 0 | Exact POC source review completed above; retain separate owner-run live evidence | Next: reproduce startup using documented working directory/configuration; identify all temporary global URLs and age changes |
| 1 | Authoritative package identity in execution snapshot and async resolver contract | Wrong hash/version/runtime/SDK rejected before module entry; untrusted/retired versions cannot create a new hosted request; built-in path remains explicit |
| 2 | Verified .NET package cache/host, integrating the reusable parts of the POC | Real archive through upload/approval/NATS/load; corruption, missing object, interrupted download, concurrent preparation, restart, dependency and deadline failures |
| 3 | Connection-aware authorized read plan and verified ForeSite current measurement | Two connections with the same external well ID reach the intended server; forged/missing source mappings rejected; settings revision/disablement/restart behavior proven; Windows session cookie and one-401 retry preserved |
| 4 | Measurement-time/quality contract plus persisted failure diagnostics | Live sanitized response fixtures, known stale/missing/denied cases, repeated unchanged measurement, UTC/offset handling, slow acquisition and multi-input partial failure; offline/live freshness policy parity |
| 5 | Small reliability correction for publication fairness and recovery | More than 20 failing oldest rows cannot starve a later valid package; retries retain work, do not duplicate audit and preserve retirement fencing. Can proceed independently of slices 1–4 |
| 6 | Direct-well setup, automatic source mapping and read-only preview | End-to-end setup without hand-entered provider IDs or one-well View; backend ownership/capability checks and scoped preview denial tests; stale setup changes require review |
| 7 | UI package upload and explicit readiness/error display | Existing global logic.manage boundary, size/type/conflict handling, duplicate-upload reconciliation and unchanged approval; distinguish published from runnable; activation scripts use reviewed If-Match |
| 8 | Python production host using the established contracts | Same package/failure/input semantics, missing interpreter/dependency, malformed/oversized output, cancellation and process-tree termination on Windows/Linux |

Keep each slice one complete reviewable behavior, using a small number of
cohesive commits. CI runs on the draft PR's current head; do not push trivial
increments solely to start another full cycle. Update the authoring-kit status
document when behavior actually lands; the older roadmap is not an accurate
inventory of today's implemented features.

## First repeatable live acceptance

Use the supported installed/dev topology, with no scratch-only code, customer
endpoints in source, or unexplained global configuration override:

1. Build and pack the approved .NET starter; record its version/hash.
2. Upload, explicitly approve, observe publication and verify worker loading of
   the exact archive. Repeated runs and service restarts identify the same bytes.
3. Choose an imported well through its saved ForeSite connection/source binding;
   preview the verified numeric input and its provenance.
4. Save, validate and activate a reviewed Shadow revision, then request it using
   the existing authorized/idempotent manual execution path.
5. Observe the input-check outcome and recorded inputs. Exercise an intentionally
   stale response and confirm a failed run retains its rejection evidence.
6. Restart the services and repeat. Disable the selected connection, change a
   relevant source setting and retire a package in controlled separate cases;
   observe the specified refusal/review behavior.
7. Assert there were zero provider write calls. This proves observation/package
   execution only; approved recommendations, provider writes, command readback
   and physical control remain separate implementation work.

CI should provide a real PostgreSQL/NATS package round trip and a recorded HTTP
ForeSite substitute; it must not require access to the on-premises server. The
owner-run Windows test supplies the separate live identity/data evidence.

## Verification limits for this review

This is a source review and planning change, not a runtime fix. The complete POC
diff, current baseline and relevant existing tests were inspected. Python
authoring discovery ran five tests: four passed and the .NET cross-runtime test
was skipped because the SDK is unavailable. Engineering conventions and staged
whitespace checks passed. No local .NET compilation, NATS/PostgreSQL integration
execution or live ForeSite reproduction is claimed. The feature itself adds no
test coverage; CI and the owner-run acceptance above remain gates on integration.
