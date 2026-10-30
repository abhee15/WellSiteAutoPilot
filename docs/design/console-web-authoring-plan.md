# Console and web module authoring plan

Status: proposed feature sequence; documentation only.
Date: 2026-10-29 (America/Chicago).
Repository baseline: develop `630f1ad4f137488d4f1260781abaf663936e2dd8`.
This plan records the current owner discussion and supersedes broader authoring
suggestions in that discussion. It does not enable execution modes or approve a
physical-control implementation.

## Goal and scope

Let a customer author Logic Modules using either the existing console/local kit
or an authoring workspace in WellSite AutoPilot. Reuse module contracts, validation,
package format, saved connections and authorization. Start with a complete,
bounded ForeSite path; apply the experience to CygNet and PetroVisor as their
actual supported capabilities become available.

Keep one application and one authoring permission across providers. Show installed
adapters under Connections. Authoring can inspect authorized live data but never
write to the source system. Test outputs and proposed actions remain in authoring.
Generic customer adapter installation, unrestricted visual orchestration, agents
and a full browser IDE are later work.

## Reviewed baseline: facts versus proposed work

| Area | Evidence at the reviewed baseline | Consequence |
| --- | --- | --- |
| Adapter inventory | IntegrationGateway/Adapters/AdapterCatalog.cs registers Simulator, ForeSite and PetroVisor in code | Reuse this inventory; do not imply CygNet is installed just because a project exists |
| Capability metadata | AdapterDescriptor and ProviderCapability declare broad capabilities; ForeSiteAdapter.Descriptor currently lists AssetDiscovery | Reconcile metadata with supported authorized reads; a descriptor alone is not a complete operation catalog or live compatibility check |
| Module boundary | ModuleSdk/LogicModuleContracts.cs accepts normalized inputs and returns engineering results, recommendation/control intents and state updates | Preserve semantic inputs and intents; modules do not receive provider credentials |
| Structured inputs | Structured manifests V2 and the bounded structured-card implementation exist | Preserve schemas and array/card shapes; do not reduce everything to scalar fields |
| Shared validation | CLI Program.cs already calls Application.Logic manifest, package-reader and compatibility code | Reuse existing shared code before extracting anything new |
| Packaging | CLI packs deterministic .wsamodule archives; approved exact-byte package hosting exists | Extract only cohesive packaging mechanics needed by a second caller; retain the package contract |
| Authorized reads | Persisted connection/source-aware read plans, Gateway session reuse and bounded diagnostics are documented in authorized-connection-reads.md | Extend through existing authority checks; an authoring preview endpoint/client still needs explicit design |
| Runtime limits | Verified .NET host documentation explicitly defers process/resource isolation; Python production host remains unavailable | Offline kit support is not production runtime support; no arbitrary draft code on the existing operational worker |
| Recommendation foundation | RecommendationMaterializer creates records from completed Recommendation executions' ControlIntents; RecommendationDecisionService records decisions without commands | Extend this behavior after tracing producers/consumers; SDK RecommendationIntentDraft is not automatically equivalent to this materialization path |
| Configured execution | ConfiguredLogicService and ExecutionService provide Shadow-focused creation/request paths | Recommendation end-to-end enablement is unfinished even though enum values and decision services exist |

Source roots for these findings:
- [Provider contracts](../../src/integration/WellSiteAutoPilot.Integration.Contracts/Providers/ProviderContracts.cs)
- [Adapter catalog](../../src/integration/WellSiteAutoPilot.IntegrationGateway/Adapters/AdapterCatalog.cs)
- [ForeSite adapter](../../src/integration/WellSiteAutoPilot.Integration.ForeSite/ForeSiteAdapter.cs)
- [SDK contracts](../../src/modules/WellSiteAutoPilot.ModuleSdk/LogicModuleContracts.cs)
- [CLI](../../tools/WellSiteAutoPilot.Cli/Program.cs)
- [Authorized reads](../implementation/authorized-connection-reads.md)
- [Structured manifests](../architecture/structured-module-manifest-v2.md)
- [Verified host](../development/verified-package-host.md)
- [Recommendation materializer](../../src/server/WellSiteAutoPilot.Application/Recommendations/RecommendationMaterializer.cs)
- [Recommendation decisions](../../src/server/WellSiteAutoPilot.Application/Recommendations/RecommendationDecisionService.cs)
- [Configured Logic service](../../src/server/WellSiteAutoPilot.Application/ConfiguredLogic/ConfiguredLogicService.cs)
- [Execution service](../../src/server/WellSiteAutoPilot.Application/Executions/ExecutionService.cs)

Older roadmap and authoring-kit documents describe earlier baselines. Their gap
lists must not override newer implementations or the code evidence above. The
owner reports successful live ForeSite Shadow testing; this review does not
independently establish all installed-environment acceptance results.

## Customer journey

1. Open Connections: Connections lists configured systems; Adapters lists adapters
   included in this deployment, versions, supported capabilities and guidance.
   Adapter availability is distinct from a particular connection's health.
2. Open Authoring from the existing Logic area, or use Create module from a
   connection. Choose the provider first; prefill it for a connection entry point.
3. Choose a starter and its supported runtime. Show what the starter requires.
   Select an authorized connection/well for deployment-specific field discovery
   or live input preview. Offline work can use samples without a live connection.
4. Configure module inputs and parameters with labels, help, units, validation
   and sample values. Keep provider credentials and server configuration in
   connection administration.
5. Validate the authored module, then Build package. For .NET, a restricted build
   host compiles the supported project against a pinned SDK and produces the
   manifest plus managed binaries/dependencies in a standard .wsamodule archive.
   Source export is an optional intermediate artifact, not completion of authoring.
6. Offer Download package and Add to module catalog using the same verified build
   artifact. Catalog submission reuses existing upload validation, identity/hash
   checks and disabled/untrusted registration. Existing upload permission is still
   required; submission does not approve, publish or activate the module.
7. Retain local console build/package workflows. Hosted draft tests remain a
   separate isolated step and do not grant production trust.

Reviewed templates can help create a starting module. They do not define the
complexity limit of authoring. The workspace design must account for the
calculation/dependency requirements below before its scope is settled. Richer
authoring, diagrams and natural-language generation require implementation;
they are not capabilities supplied by the current manifest format.

## Owner correction: the wizard is insufficient

The owner reviewed the conversation mock and rejected its simplicity as a model
for real Logic Module authoring. A Start/Configure/Build wizard is insufficient
for modules with dependent parameters, multiple calculations and intermediate
results. Record the existing mock as an incomplete package-flow illustration,
not the accepted authoring design or a feature-completion criterion. Simple
setup assistance may remain, but must lead into a revisitable workspace.

### Sample review and design consequences

The following implementations were read, including their associated manifests.
This is source review, not new execution or live-provider verification.

| Repository evidence | What it actually does | Requirement for the next design |
| --- | --- | --- |
| [WellInputCheck (.NET)](../../modules/authoring/dotnet/WellInputCheck/EntryPoint.cs) and [Python equivalent](../../modules/authoring/python/well_input_check.py) | Validate one pump-fillage observation, identity, units and context; return the observed value | Useful onboarding/contract fixtures, not representative of the full calculation-authoring requirement |
| [CardCalibrationModule](../../modules/samples/structured-cards/CardCalibrationModule.cs) and [manifest](../../modules/samples/structured-cards/manifest.json) | Consume a card collection plus a separate calibration record; validate paired array lengths, identity and capture times; transform every load sample using scale/offset; derive per-card peaks and an aggregate peak; return curves and a scalar | Support structured inputs, configuration dependencies, repeated calculation over collections, named intermediate values, aggregation, multiple output shapes and failure paths |
| [ForeSite card-summary POC](https://github.com/abhee15/WellSiteAutoPilot/blob/poc/cards-e2e/modules/poc/foresite-card-summary/EntryPoint.cs) | Select CURRENT cards, reject missing/misaligned input, and derive card count, load extrema, stroke travel and newest-card age | Show selection/conditional logic and several derived outputs with their source/time semantics; identify this as POC evidence rather than current production behavior |

The calibration example is synthetic linear calibration, not a downhole-card
solver. These samples establish a minimum design exercise, not proof that the
repository already contains every complex customer algorithm. The SDK permits
algorithm code inside ExecuteAsync; manifest input/output declarations do not
describe that code's internal calculation graph. Do not infer a complete visual
workflow or reconstruct dependencies from the manifest alone.

### Requirements to carry into the revised concept

- Make inputs, parameters, calculations, dependencies and outputs revisitable in
  one workspace. An author must be able to name an intermediate result, see what
  consumes it, and trace an output back through its calculations to its inputs.
- Represent formula dependencies, conditional branches, collection operations
  and aggregation; account for custom algorithm code through the existing SDK
  rather than restricting every module to threshold templates. Decide which
  operations the first visual experience can author and how code-backed steps
  are represented. Do not promise unrestricted browser code execution.
- Preserve arrays, records, units, timestamps, quality and asset/source identity.
  Distinguish invalid/missing inputs from a valid calculated zero. Show validation
  and test failures at the relevant input or calculation, not only at final build.
- Show external data and calculation dependencies as named roles, including the
  ForeSite-to-WAMI scenario below. One provider selector must not define the
  entire module. Configuration changes must invalidate affected test/build evidence.
- Let tests inspect intermediate values, branch outcomes and final outputs.
  Detailed traces require a versioned reporting/instrumentation contract; the
  current SDK result envelope does not expose arbitrary local variables.
- Retain console and web clients, standard manifest/binary packaging, separate
  approval and read-only live-data access with simulated proposed actions.

Proposed visual direction to review: a module outline/dependency view, a central
calculation workspace, contextual input/parameter properties, and a test/results
area. A dependency view does not itself select a new workflow engine or commit
the product to an unrestricted visual language. Explore the representation
against the samples before choosing a canvas, structured editor, or hybrid.

Next design/probe acceptance: walk through card calibration end to end, including
paired-array failure and peak aggregation; then a composed example with named
intermediate calculations and a ForeSite source plus a WAMI-shaped calculation
stub. Demonstrate editing an upstream parameter, tracing affected outputs and
invalidating stale results. Preserve the underlying algorithm when moving
between console and web; explicitly report unsupported authoring operations.
The simple .NET build/package probe remains useful evidence about packaging,
but cannot validate this richer authoring experience. No new runtime or visual
authoring capability is implemented by this note.

## Near-term design and probe requirement: multiple providers

Owner clarification: a Logic Module must not be permanently restricted to one
provider, adapter or saved connection. The first ForeSite starter is an MVP
scope choice, not the final module cardinality. A module may need several
connections of the same provider as well as connections to different providers.

Example: ForeSite supplies well configuration, measurements and a model file;
WAMI supplies a calculation capability that consumes those inputs; the module
uses the returned engineering results to produce an output or proposed action.
WAMI here is a calculation service, not an assumption that a general workflow
orchestration engine already exists. ForeSite configuration/model retrieval and
this cross-provider calculation path are capabilities to examine in the next design/probe pass; they are not implemented.

Design direction:
- Represent dependencies by named roles and capability/input requirements, with
  deployment-specific saved connections bound separately. A single provider
  selector in the first UI represents the first source, not a permanent module-
  wide identity. Later allow adding data and calculation dependencies.
- Keep data acquisition, governed calculation invocation and final output/action
  routing distinct. Runtime/adapter boundaries perform provider I/O; modules do
  not receive credentials or independently call provider services.
- Associate sources explicitly with the intended logical well/asset. Similar
  names or matching external IDs across connections do not establish identity.
  Check authorization and compatibility independently for every dependency.
- Preserve schema/unit/time/source provenance, connection revisions and calculation
  service/model versions. Model files need a supported artifact contract and
  bounded transfer; do not assume the existing inline scalar/card envelope can
  carry arbitrary files.
- Probe whether required data can be acquired before module evaluation or whether
  the algorithm needs governed intermediate calculation requests. Establish
  timeout, cancellation, failure and test/replay semantics before choosing an
  execution mechanism; do not introduce a general workflow engine now.
- Authoring tests must capture proposed writes and isolate production state even
  when several services participate. Calculation operations must be checked for
  side effects rather than assumed safe from their name.

Current limitation: LogicModuleManifest exposes a single optional RequiredAdapterKey,
and current view-based configuration is bound to a connection. These are existing
single-provider implementation constraints, not evidence that multi-provider
execution is supported. Review manifest/configuration/read-plan/SDK evolution in
the next scoped design/probe pass; do not replace RequiredAdapterKey with an unvalidated list
or change current live routing in this documentation PR.

Acceptance for today's design: use a single-provider starter as the first working
example, while making multi-provider dependencies part of the immediate design
and probe agenda. Before settling the authoring contracts, probe one ForeSite
source plus a WAMI-shaped calculation dependency, using a clearly labeled stub
where a real service or model contract is unavailable. A stub proves contract
composition only, not WAMI integration. Identify touched single-provider
assumptions and resulting manifest/configuration/read-plan/SDK changes. Full
implementation follows the findings; do not treat this requirement as a distant
post-phase-one enhancement.

## One core, two clients

| Responsibility | Shared behavior | Console | Web |
| --- | --- | --- | --- |
| Manifest/schema validation | Existing bounded validators and stable errors | Local library calls | Server calls the same validators |
| Package inspection/compatibility | Existing reader, hash and runtime/SDK checks | validate/verify/preflight | Existing server package path |
| Build and package | Pinned module SDK, supported project inputs, shared archive writer and preflight | Local .NET SDK compilation and CLI packaging | Restricted build host compiles binaries, packs and verifies the same format |
| Deployment discovery/preview | Same authenticated Server authority and saved connection semantics | Future authenticated client commands | API-backed guided controls |
| Offline evaluation | Versioned fixtures and common result semantics | Retain current kit support | Recorded-input testing only after a supported execution host exists |
| Publication | Existing immutable catalog approval/publication | Existing supported delivery path | Existing upload/approval UI |

Do not make the browser execute .NET libraries or require a new microservice for
pure validation. Do not route either client directly to ForeSite, CygNet or
PetroVisor for the new authoring live-read experience. Existing diagnostic probe
utilities are not the authorization architecture for customers.

Source projects, authoring descriptions, executable packages and test reports are
different artifacts. Maintain a common import/export description so switching
clients does not require re-entering inputs and parameters. Do not promise
identical ZIP bytes for independently compiled binaries; determinism applies to
packing the same manifest and payload.

## Permissions and execution boundary

Add one authoring permission to the existing configurable permission catalog
(proposed name: `modules.author`; verify naming during implementation). Do not
create ForeSite/CygNet/PetroVisor authoring permissions or a hardcoded Author role.

Authoring permission grants workspace/draft operations. Existing connection and
paired permission/Asset scope checks still govern live metadata and data access.
An author need not receive connection administration rights. Public adapter
descriptions exclude secret settings; discovering a measurement does not grant
authority to acquire it. Preview reuses server checks and current revocation rules.

Approval, activation and physical execution remain separate authorities. Draft
tests do not create operational recommendations, ControlActions or production
state updates. Capture proposed intents locally to the test result. Installing
or approving a package is not equivalent to authorizing source access.

Never execute untrusted customer drafts in the Server, Gateway or existing
in-process operational worker. Hosted draft execution requires a separately
restricted process identity, network/filesystem access, time/resource limits and
cancellation enforcement. Authorization at an SDK boundary alone is insufficient.
Initial inventory/preview work does not execute customer code. Building also needs
isolation: project targets, analyzers and restore hooks can execute code during
compilation. Build and test hosts receive no operational credentials. A normal
child process or collectible assembly context alone is not an isolation boundary.

## Small implementation slices

| Slice | Deliverable | Completion evidence |
| --- | --- | --- |
| A: inventory and capability preview | Adapters tab, general authoring permission, supported-operation metadata and authorized ForeSite preview | Actual adapter inventory; no secrets; scoped/revoked access tests; no provider writes |
| B: author, build and deliver | Revised calculation/dependency workspace validated against the sample scenarios; restricted .NET build, manifest/binaries package, Download and Add to catalog | Scope follows the revised design/probes; a template wizard alone does not complete authoring; same validation in CLI/web; verified artifact supports download/submission; catalog remains untrusted until separate approval |
| C: isolated draft testing | Supported .NET draft runner using fixture or explicitly authorized live input snapshots; bounded reports | Timeout/cancellation/isolation checks; report retains module/input identity, units, quality and time; proposed actions never reach provider commands |
| D: broader authoring | Additional actual CygNet/PetroVisor operations/templates and runtime support | Provider-specific acceptance evidence; Python execution only after a real production/test host is implemented |
| Later | Agent-assisted creation, diagrams and customer adapter SDK/installation | Reuse established contracts and validation; separate scoped design |

Do not assume a generic workflow language or browser IDE is necessary to deliver
A/B. Custom calculations remain supported through the console and local SDK,
and the revised web design must account for their representation and handoff.
Introduce shared libraries only when two real callers need them; no general framework or
second credential/package store.

For draft storage, begin with an exportable authoring description and local
console projects. Server-side saved draft ownership/sharing and report retention
belong to slice C's concrete design, not a new generalized collaboration system.
Any saved live-data report retains access controls and is subject to export rules.

## Priority relative to ForeSite operating modes

Recommended sequence, subject to the owner's product priority decision:

1. Close any remaining live Shadow acceptance issues from owner testing.
2. Deliver bounded authoring A/B so a customer can discover supported inputs and
   build and download or submit a valid module package using either client.
3. Complete the ForeSite Recommendation workflow using existing materialization,
   decisions, pinned package/source evidence and scoped result UI. Define whether
   approval merely records an operator decision or is intended to request a
   physical command. Start with advisory behavior; do not equate approval with
   dispatch. Specify handling of expired recommendations and changed inputs.
4. Add isolated draft testing C when customer code execution is needed. Its order
   relative to step 3 can change with the immediate customer need.
5. Implement autonomous control only after a dedicated control-path design and
   deployment acceptance: command policy, fresh authority/readiness checks,
   ownership/conflict handling, inhibit/stop behavior, durable action identity,
   ambiguous dispatch recovery and readback verification. Scheduled live reads
   also need the explicit service-account design already identified in the repo.

Reasoning: A/B makes existing Shadow capability usable without waiting for a full
authoring platform. Recommendation provides the next operator-facing operational
value. Autonomous control should build on verified command behavior; it should
not be enabled by changing an enum or adding a UI mode selector.

If the immediate customer already authors successfully with the console and
needs operating outcomes, Recommendation can precede A/B. This plan recommends
A/B first for the customer journey discussed, not a blanket requirement to finish
authoring before operational modes.

## Visual design before probes

Use the existing application shell with Logic > Authoring and Connections >
Adapters. The initial Start/Configure/Build conversation mock is insufficient and
must be revised against the sample/dependency requirements above before feature
implementation. Explore a persistent calculation workspace, using progressive
disclosure to keep complex logic understandable rather than reducing its expressiveness.
Retain sample/live input selection, labeled parameters, clear proposed outputs,
and Download/Add to catalog after a verified build. Show build pending/cancel/failure
states and invalidate the artifact on every package-affecting edit. Upstream
provider/connection changes clear dependent fields and preview evidence.

The shell must reuse current grouped navigation, route/tab permissions, shared
controls, localization, skip-link and mobile focus behavior. The simplified
conversation mockup is not a replacement shell implementation.

## Independent advanced-model review

An independent GPT-6 Astra review examined this plan, the conversation mockup,
current UI route/tab authorization, SDK contracts and read/package boundaries.
Conclusion: ready for disposable probes, not for shipping hosted builds. No
probe execution or live verification was performed by the reviewer.
That review predates the owner's wizard correction. It does not approve the
revised complex-authoring UX; that design still needs its own review and probes.

| Finding | Required response |
| --- | --- |
| Restricted Windows builds are unproven | Use reviewed server-owned templates and allowed parameters/dependencies; prove identity, filesystem/network isolation, resource limits, process-tree cancellation and cleanup. Relocate builds if same-host isolation cannot be enforced |
| Authoring has no operational execution read plan | Probe explicit authoring preview using the current caller, saved connection revision, scoped Asset and supported quantity; reuse authority/Gateway mechanics without creating operational runs or recommendations |
| Existing Logic routing requires catalog/configuration read permission | Extend route/tab visibility for authoring-only users; offline authoring must not require administrative or catalog read rights |
| Administrative connection DTOs contain settings | Return a minimal authorized connection projection to authors; never reuse settings-bearing responses blindly |
| Build artifacts and previews can become stale | Bind completed artifacts to the full draft revision; clear dependent choices/previews after upstream edits; gate steps and show pending/failure/cancel states |
| Recommendation output channels differ | Name the starter as a proposed-adjustment example; explicitly choose its SDK output profile and verify it in the round trip. Compilation alone does not create operational recommendations |

That earlier review used a ForeSite-only implementation example. The subsequent
owner requirement for multi-provider dependencies takes precedence for contract
design and probes; this paragraph does not permit a permanent single-provider limit.
Other providers are enabled when their actual required capabilities are supported. The independent review did not identify a
reason to add a generic workflow engine, adapter marketplace or Python host to
this first feature.

## Probe gate before feature implementation

The next stage after this documentation is disposable, bounded probes, with
results recorded here or in a linked probe report. Feature implementation starts
only after probe review resolves known blockers for its scope; untested areas
remain explicit rather than claiming that no gaps exist.

| Probe | What to establish | Evidence required |
| --- | --- | --- |
| Build host | On a representative Windows/on-prem deployment, build one supported .NET starter with pinned SDK/target and allowed dependencies; evaluate preinstalled SDK versus a separate restricted build worker | SDK availability, offline restore/cache needs, build duration, actionable errors, cancellation, cleanup, restricted identity/network/filesystem behavior |
| Package round trip | Generated manifest plus actual compiled payload through existing pack, verify and host preflight | Correct entry point, module identity/version, schemas, managed dependencies and hash; failed build cannot offer a successful package |
| Download/catalog handoff | The same successful immutable build artifact supports download and existing catalog ingestion | Matching bytes/hash; identity/version conflicts handled by existing rules; catalog submission stays untrusted and requires existing authority |
| Console/web reuse | Prepare the same starter inputs using both clients and existing validators | Equivalent manifest semantics and errors; import/export preserves inputs/parameters; no duplicate package validator |
| Multi-provider composition | ForeSite data/configuration/model requirements plus a separately bound WAMI calculation dependency; start with an explicit stub if needed | Named dependency bindings, per-source authority, asset correlation, input/output schemas, single-provider assumptions and limits documented; no claim of live WAMI support |
| Capability/preview | Actual ForeSite supported operations versus descriptors, authorized input selection and preview | Connection-scoped scalar/structured results; no credentials or provider write calls |
| Authoring workspace UI | Revised concept exercised against card calibration and composed calculations, using shared controls | Revisit inputs/calculations; trace intermediate dependencies and branches; inspect structured/test results; invalidate affected evidence; build/package actions and accessible keyboard/mobile flow |

Begin with a constrained .NET starter project, pinned dependencies and no arbitrary
customer build scripts. Determine the enforceable boundary in the probe, rather
than assuming a template makes compilation safe. Unrestricted source/projects
need a later reviewed expansion. Do not install the entire build toolchain into
the operational Server merely to satisfy the web UI.

Build failure leaves an editable draft and safe diagnostics; catalog actions
are available only for a completed verified artifact. Pin the draft revision to
the artifact so edits require a fresh build. Building is distinct from executing
the module and neither grants approval. A statically valid draft can build without
executing tests. Record validation, test (not-run/passed/failed/stale/cancelled) and
verified-package states separately; do not make slice B implicitly depend on the
isolated execution host deferred to C.

This larger completion criterion can change the earlier priority recommendation:
if SDK deployment, offline builds or isolation block a small authoring release,
complete the advisory ForeSite Recommendation workflow while resolving the build
design. Do not silently replace the requested binary package with source export.

## Initial probe evidence

[Probe PR #321](https://github.com/abhee15/WellSiteAutoPilot/pull/321) records
the first trusted .NET build/package check against the reviewed baseline.
On local Linux x64 with SDK 10.0.100, all 13 probe checks passed: actual binary
compilation, existing manifest validation and pack/verify/preflight, payload-byte
preservation, repeat packing, wrong-hash rejection, warmed-cache no-restore build,
compiler-failure diagnostics and temporary workspace cleanup. The existing
authoring suite also passed all 11 tests with .NET and package checks enabled.

This evidence does not prove restricted Windows execution, a clean offline
installation, authenticated artifact delivery/catalog ingestion, or live authoring
preview. The original probe head `5604bf8db859bc411112c6c26d614e18aecb4bd7`
passed all nine CI checks, including the Windows and Linux authoring jobs. Those
jobs execute the trusted build/package probe; this confirms runner compilation,
not deployment isolation. No feature is enabled.
The [probe README](https://github.com/abhee15/WellSiteAutoPilot/blob/probe/authoring-build-package/modules/authoring/probes/README.md)
links the recorded report and outstanding questions.

## Targeted confidence review: implementation remains on hold

The extended [probe evidence and review](https://github.com/abhee15/WellSiteAutoPilot/blob/probe/authoring-build-package/modules/authoring/probes/CONFIDENCE-REVIEW.md)
records two independent advanced-model review passes and the resulting changes.
Local results: 19 real-sample/synthetic-dispatch checks, 13 candidate-document
assertions, and browser probes at 1440/390/320px. The browser now creates/edits a
bounded scalar dependency, changes a condition, rejects a cycle and downloads a
document that a Node reference harness imports/exports before browser retesting,
including nondefault parameters. This is candidate-document/reference-semantics
evidence, not compatibility with the existing .NET CLI or arbitrary code import.

The existing dependency-free starter also restores and compiles with cleared
NuGet feeds and an empty package cache using a preinstalled SDK. This does not
prove customer SDK installation or offline provisioning of CLI/Application and
customer dependencies. Extended Windows/Linux CI is configured to rerun these
new probes; consult the current probe head and retained reports for its results.

The ordinary-child boundary measurement deliberately remains blocked: the child
can read a synthetic sibling file and reach a synthetic loopback service. A
working directory and subprocess do not enforce build/test isolation.

These findings refine the next gates before feature implementation:

1. Define an authoring project document distinct from the manifest/package, with
   stable typed dependencies, conditions and opaque versioned SDK-code steps.
   Collection/custom algorithms remain first-class requirements; two scalar
   operations are a probe scope, not the product's expressiveness limit.
2. Choose authoritative numeric/unit/validation semantics and shared client
   conformance. The browser's finite JS doubles are not the sample's checked
   .NET decimals. Preserve code-backed steps; never infer arbitrary algorithm
   internals from a manifest or promise lossless visual conversion of all code.
3. Set ownership and contracts for governed intermediate calculation requests,
   bounded model artifacts, cancellation, failures and ambiguous completion.
   The stub coordinator proves synthetic dispatch preconditions only: it does
   not acquire protected inputs through current Server authority.
4. Probe actual-authority authoring preview, minimal connection metadata and
   draft/source revocation without creating operational execution state. Then
   prove authenticated, revision-bound download and exact-byte catalog ingestion.
5. Establish restricted-host evidence on the intended deployment before hosted
   compilation/testing: identity, secret denial, filesystem/network policy,
   resource limits, complete process-tree cancellation and cleanup.

The disposable workspace is not a production React shell or a settled graph
compiler. Build-state interactions are simulated; sample arithmetic and code
execution are tested separately. Existing SDK outputs do not expose arbitrary
intermediate variables. A reporting/instrumentation contract still needs design.
No green CI result or synthetic test clears these remaining product/host gates.

## Review iterations and verification

Pass 1 mapped the customer journey to SDK, manifest, packaging, connections and
authorization. It removed the separate web application and per-provider authoring
permissions and retained both console and web clients.

Pass 2 challenged the design against current code. It separated draft testing
from production trust, identified missing process isolation, corrected the claim
that Recommendation is wholly absent, and deferred the generic adapter platform.

Pass 3 incorporated the owner's simplification and checked priority dependencies.
It identified ForeSite descriptor/read-path drift, distinguished workflow
definitions from executable packages, and kept preview separate from execution.
The owner's subsequent clarification makes compilation and package delivery part
of the first complete authoring workflow; source-only export is intermediate.

Before implementation, run a small contract/UI probe using existing shared
controls. Verify provider selection, empty/unavailable capabilities, authorized
field preview, parameter validation, calculation dependencies, intermediate-result
inspection and keyboard/mobile flow. Then implement
small PRs with current-head CI. Use existing ActionButton, SearchInput, Status,
localization and focus behavior; generate API clients from the server contracts.

Relevant automated checks: CLI/web validation parity; scalar/structured schema
preservation; no cross-connection fallback; unauthorized preview denial; expired
authority; export/import fidelity; no credentials in artifacts; and no command
calls from authoring. Add host isolation tests only with the host slice. Live
ForeSite acceptance remains owner-run; CygNet/PetroVisor need their own evidence.

This documentation change has no schema, API, runtime, installer or production
behavior impact. Source paths and relative links are reviewed; it does not claim
a full security audit, new test execution, live provider verification or completed
feature implementation.
