# Targeted authoring probes and confidence review

Status: disposable probes only; full authoring implementation remains on hold.
Source baseline: PR #321 at `5604bf8db859bc411112c6c26d614e18aecb4bd7`.
Relevant runtime sources were compared with develop `c384610`; they were unchanged.
No new Server endpoint, SDK contract, module runtime, provider routing or installer is implemented.

## Evidence and release gates

| Question | Actual evidence | Verdict / remaining gate |
| --- | --- | --- |
| Can existing .NET code build and package? | Original trusted-template probe passed on Windows and Linux; shared CLI validates, packs, verifies and preflights real binaries | Feasible for that trusted template; not a hosted customer-build release |
| Can a complex sample calculate over structured inputs? | Composition probe invokes the real CardCalibrationModule through the real structured host; 490 peak changes to 250 after a scale edit; array mismatch is rejected | Demonstrated for this synthetic sample, including its ragged curves and capture metadata |
| Can bindings govern a composed call? | Disposable coordinator checks each named binding and synthetic permission, pins source/revision/asset/schema/model and hash-bounded model bytes, then calls a WAMI-shaped arithmetic stub; changed pins/denied dependency prevent dispatch | Synthetic dispatch preconditions demonstrated. Not protected data acquisition: actual sample fixture evaluation happens before the coordinator. No live ForeSite/WAMI or production multi-provider authority established |
| Can a user create a dependency rather than fill a wizard? | Browser creates a named difference step, changes its dependency, rejects a self-cycle, changes a condition and inspects resulting values at 1440/390/320px | Bounded scalar dependency editing demonstrated. Collection transformations remain an opaque sample-code step; arbitrary algorithms/collection authoring are not solved |
| Can a document move between clients? | Browser downloads real JSON; Node reference harness imports, evaluates and exports it; browser imports and retests, including nondefault parameters | Candidate-document transport and shared JS reference semantics demonstrated. This is not the existing product CLI, a graph compiler, arbitrary source import or .NET decimal parity |
| Do changes/cancellation invalidate evidence? | Browser discards a stale async result; edits clear results and simulated build evidence; failed/cancelled builds cannot export an artifact; condition display tracks its edit | Fixture-only interaction evidence. Real job cancellation, complete process-tree cleanup and product route/accessibility acceptance remain separate |
| Can the starter build without a NuGet cache/feed? | Copy existing starter + SDK sources to a temporary tree; clear feeds and use an empty package cache; restore and compile using an already installed SDK | Demonstrated for the dependency-free starter. Does not prove SDK installation, offline Application/CLI dependency provisioning, arbitrary analyzers/packages or network denial |
| Is an ordinary child isolated? | Child reads a synthetic sibling file and connects to a synthetic loopback listener | **Blocked.** Working-directory/subprocess restrictions are insufficient. Representative restricted Windows identity, filesystem/network policy, resource limits and process-tree cancellation still need host evidence |
| Can a Windows AppContainer + Job object contain a child? | `windows_isolation_probe.py` (local Windows 11): child in a capability-less AppContainer granted only its work directory; read outside, write outside and loopback connect were all denied, work-dir write succeeded; closing a kill-on-close Job (512 MB process limit) terminated the child and its grandchild | **Mechanism viable, gate still blocked.** Measured with PowerShell as a stand-in child on a developer account; memory-limit enforcement, the real SDK/compiler under AppContainer, secret denial and the intended server identity are not shown |
| Can authoring preview reuse existing reads unchanged? | Source trace below finds execution-specific authority and no authoring preview API | **Unresolved integration gate.** Design a caller-authorized, minimal preview path without operational execution state |
| Can a build be downloaded/submitted under current authority? | Existing package service validates/hash-identifies exact bytes and registers disabled/untrusted; existing persistence tests cover authorization and immutable bytes | Reusable ingestion foundation. No authoring build store, draft-bound download endpoint or authenticated build-to-catalog handoff exists; end-to-end authoring delivery remains unproven |

Local machine-readable evidence lives in `evidence/`. CI runs the .NET composition,
candidate document, empty-cache and synthetic boundary probes on both operating
systems and retains fresh reports. A green boundary-probe job means measurement
completed; its report intentionally retains `releaseGate: blocked`.

## Source trace: integration still needs design

- `LogicModuleManifest.RequiredAdapterKey` is one optional module-wide adapter;
  it does not declare named calculation-service dependencies or model artifacts.
- `ConfiguredLogicService.ResolveViewAsync` validates a View's single Connection.
- `ExecutionRepository.Reads.PinReadSourcesAsync` pins owner Connection, source
  binding and revision for supported ForeSite reads. It requires execution-request
  and configured-logic-read authority. `ResolveReadAsync` also requires an execution,
  active configuration, expiring read plan and fresh authority. Creating an
  operational run merely to preview a draft is not the authoring design.
- `Server/Connections/ConnectionMapping.ToResponse` includes settings. An author
  needs a minimal authorized connection/capability projection, not this admin DTO.
- `LogicModulePackageService.UploadAsync` preserves candidate bytes, computes the
  reviewed hash and passes a disabled/untrusted module to `UploadAuthorizedAsync`.
  `SecurityPersistenceTests/AuthorizedModulePackageChecks` exercises scope denial,
  exact-byte storage, immutable versions and separate approval. These are existing
  catalog guarantees, not evidence of an authoring build/download endpoint.
- The SDK result includes final engineering values/intents/state, not arbitrary
  internal variables. The probe's trace is explicitly authored. Production
  intermediate evidence requires an intentional instrumentation/report contract.

## Review iterations and changes

Pass 1 ran the sample and inspected the workspace. An independent advanced-model
review rejected inert dependency metadata and a fixed formula viewer as evidence
of multi-provider composition or complex authoring. It also found that requiring
test success before Build conflicted with the plan's later isolated-test slice.

Pass 2 made bindings operative in a synthetic coordinator; added pin/model and
independent denial negatives, post-dispatch cancellation, editable typed scalar
dependencies, cycle/condition checks and real document downloads/imports. Build
validity is now independent from test execution: a valid draft can build with an
explicit not-run/failed/stale test status. Build never grants approval.

The second independent pass caught node-ID coercion, a console harness ignoring
imported parameters, and stale displayed branch logic. Explicit string-ID checks,
null/missing-ID cases, shared reference evaluation of imported parameters,
nondefault round trips and condition-display assertions address those findings.
Exports identify fixture hash/case and test status. Neither independent review
certifies production isolation, authority or complete authoring readiness.

## Plan decisions before implementation

1. Define an authoring project artifact separately from the manifest/package.
   Use stable named inputs/outputs, explicit typed dependencies and branches;
   retain code-backed SDK steps as opaque, versioned artifacts. Do not infer
   arbitrary internal graphs from manifests or discard unsupported source code.
2. Choose authoritative numeric/unit/validation semantics. The browser reference
   uses finite JS doubles; the real sample uses checked .NET decimals. Identical
   happy-path outputs do not establish algorithm parity. Production clients must
   share authoritative validation/evaluation or verified conformance fixtures.
3. Specify ownership of governed intermediate calculation calls. The current SDK
   accepts supplied inputs and has no WAMI service capability. Decide the narrow
   coordinator/contract needed, including model artifacts, timeouts, side effects,
   cancellation and ambiguous completion, before changing SDK/read-plan contracts.
4. Design authoring preview and build delivery against real caller authority;
   probe revocation, stale source/draft revisions, data export rules and exact
   artifact download/ingestion without creating operational recommendations/state.
5. Prove a restricted build/test host on the intended deployment. Ordinary child
   processes and passing CI do not satisfy this gate. Customer code must not run
   in Server/Gateway/the operational worker to work around the missing boundary.

Next probes should resolve these specific contracts and host gates, rather than
expanding this disposable scalar interpreter into a generalized workflow engine.
No statement of full implementation readiness is justified by this evidence.
