# Console and web module authoring plan

Status: proposed feature sequence; documentation only.
Date: 2026-10-29 (America/Chicago).
Repository baseline: develop `630f1ad4f137488d4f1260781abaf663936e2dd8`.
This plan records the current owner discussion and supersedes broader authoring
suggestions in that discussion. It does not enable execution modes or approve a
physical-control implementation.

## Goal and scope

Let a customer author Logic Modules using either the existing console/local kit
or a guided workspace in WellSite AutoPilot. Reuse module contracts, validation,
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
5. Validate and export the starter project/manifest and a small authoring
   description. The first web slice does not claim to compile arbitrary code.
   A compiled executable payload is still required for an executable .wsamodule.
6. Build locally with the supported kit and use the existing package
   upload/approval path. Later isolated web tests may execute drafts without
   making them production-approved.

The initial guided experience configures reviewed templates. Arbitrary rules,
diagrams and natural-language generation require additional implementation;
they are not capabilities supplied by the current manifest format.

## One core, two clients

| Responsibility | Shared behavior | Console | Web |
| --- | --- | --- | --- |
| Manifest/schema validation | Existing bounded validators and stable errors | Local library calls | Server calls the same validators |
| Package inspection/compatibility | Existing reader, hash and runtime/SDK checks | validate/verify/preflight | Existing server package path |
| Package construction | Extract deterministic archive writer when needed | Local files and compiled payload | Export sources first; server packing only when a complete supported payload exists |
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
The first slice exports drafts and previews data without executing customer code.

## Small implementation slices

| Slice | Deliverable | Completion evidence |
| --- | --- | --- |
| A: inventory and capability preview | Adapters tab, general authoring permission, supported-operation metadata and authorized ForeSite preview | Actual adapter inventory; no secrets; scoped/revoked access tests; no provider writes |
| B: starter preparation | Guided ForeSite template, input/parameter forms, validation and downloadable source/manifest description | Same manifest errors in CLI/web; console/web import/export parity; template compiles and validates in CI |
| C: isolated draft testing | Supported .NET draft runner using fixture or explicitly authorized live input snapshots; bounded reports | Timeout/cancellation/isolation checks; report retains module/input identity, units, quality and time; proposed actions never reach provider commands |
| D: broader authoring | Additional actual CygNet/PetroVisor operations/templates and runtime support | Provider-specific acceptance evidence; Python execution only after a real production/test host is implemented |
| Later | Agent-assisted creation, diagrams and customer adapter SDK/installation | Reuse established contracts and validation; separate scoped design |

Do not build a generic workflow language or browser IDE to deliver A/B. A custom
calculation can still be developed through the console and local SDK. Introduce
shared libraries only when two real callers need them; no general framework or
second credential/package store.

For draft storage, begin with an exportable authoring description and local
console projects. Server-side saved draft ownership/sharing and report retention
belong to slice C's concrete design, not a new generalized collaboration system.
Any saved live-data report retains access controls and is subject to export rules.

## Priority relative to ForeSite operating modes

Recommended sequence, subject to the owner's product priority decision:

1. Close any remaining live Shadow acceptance issues from owner testing.
2. Deliver bounded authoring A/B so a customer can discover supported inputs and
   prepare a valid module using either client.
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

## Review iterations and verification

Pass 1 mapped the customer journey to SDK, manifest, packaging, connections and
authorization. It removed the separate web application and per-provider authoring
permissions and retained both console and web clients.

Pass 2 challenged the design against current code. It separated draft testing
from production trust, identified missing process isolation, corrected the claim
that Recommendation is wholly absent, and deferred the generic adapter platform.

Pass 3 incorporated the owner's simplification and checked priority dependencies.
It narrowed the first release to templates/source export, identified ForeSite
descriptor/read-path drift, distinguished workflow definitions from executable
packages, and kept a live preview distinct from arbitrary code execution.

Before implementation, run a small contract/UI probe using existing shared
controls. Verify provider selection, empty/unavailable capabilities, authorized
field preview, parameter validation and keyboard/mobile flow. Then implement
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
