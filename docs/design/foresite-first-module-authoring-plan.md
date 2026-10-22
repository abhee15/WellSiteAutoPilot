# ForeSite-first module authoring kit and end-to-end plan

Status: implementation proposal capturing the owner discussion on 2026-10-22.
This document defines the next work; it does not claim the kit, test endpoint,
Python execution, or live ForeSite reads are implemented or verified.
Priority: complete one ForeSite read-only path before extending CygNet execution.

## Scope and author experience

Authoring happens in an engineer's editor. No separate authoring application is
needed initially. Provide a versioned authoring kit under
`modules/authoring/python/` (proposed folder), with a quick start, template,
manifest/schema guidance, sample input/output fixtures, local runner and packaging
instructions. These are implementation deliverables, not files supplied by this
docs-only change. Services engineers are the initial audience; customer authors
use the same contract later under the applicable trust policy.

An author declares required inputs and parameters, implements a calculation and
returns typed results and proposed changes. AutoPilot owns source resolution,
authentication, adapter I/O, execution modes, persistence and command dispatch.
A ForeSite-restricted module stays on its configured ForeSite connection; it
must not automatically fall back to CygNet.

## Existing evidence and limits

- Develop already has a provider-neutral module SDK, an approved built-in .NET
  sample, configured manual Shadow execution and input provenance.
- ForeSite discovery reads the source well ID. Import stores it in
  SourceBinding.ExternalId with ConnectionId, ResourceKind and local AssetId.
  Reuse this identity rather than adding a second hidden well-ID field.
- Current ForeSiteAdapter advertises AssetDiscovery. It does not implement
  the module's current-input collection.
- The Python worker main.py currently starts and waits for shutdown. It does
  not execute module packages. Python support must be delivered and verified,
  not inferred from the folder's existence.
- Module catalog registration alone is not executable package installation.
  Distribution, loading and trust remain explicit dependencies; ADR-007 is
  proposed and is not adopted by this document.
- Reviewed owner reference files: RRLPoweShellScript 2.ps1 and
  RRL_OverNetwork 2.py, shared on 2026-10-20. Both authenticate and read
  autonomous-control configuration. They are integration examples, not
  immediately compatible AutoPilot modules. Their test calculations include
  hardcoded/random values and are not production algorithms.

### ForeSite calls evidenced by the supplied scripts

| Purpose | Observed call | Verification still required |
| --- | --- | --- |
| Session | GET TokenService/CreateToken | Existing adapter session behavior is the starting point |
| Well detail | GET Wells/Well/{wellId} | Actual DTO and authorization on target installation |
| AUC configuration | POST WellAutoSetPointService/GetAutonomousControlConfiguration, body WellId array | Configuration prerequisites, selected logic identity, input values and provenance |
| Setpoint metadata/current values | GET Surveillance/Well/SetPointsParams with WellId, dgType, strOrdinal and scanOption | Meaning of scanOption, units, timestamps, quality and original-value semantics |
| Save optimization | POST WellAutoSetPointService/SaveAutonomousControlOptimizationData | Whether saving has downstream side effects or automatic processing |
| Send setpoints | POST Surveillance/SendSetPointsParams/true | Actual actuation, permission, receipt and verification semantics |

The PowerShell example also references AllSetPointParams. Do not assume these
read endpoints are interchangeable. API names above are script evidence only;
no live ForeSite service was exercised during this review. Confirm whether the
first module should depend on existing ForeSite AUC configuration or use
AutoPilot-owned parameter/input configuration. Do not accidentally require
duplicate logic setup in both products.

## Proposed execution contract

Use the existing SDK semantics as the source of truth; define a versioned Python
serialization and entry-point contract rather than inventing a second SDK.

- One well per initial invocation. AutoPilot resolves local AssetId to the
  exact saved connection and external well ID; do not search by well name.
- Context includes contract/module/execution identity, evaluation time in UTC,
  well context, validated parameters and declared engineering inputs.
- Inputs carry quantity, units, source time, quality and provenance. Missing
  quality/time must be explicit; never manufacture fresh/good telemetry.
- Output contains typed engineering results, reasons/logs and optional
  recommendation/control intents. No direct provider writes from author code.
- Define parameter/input/output schemas, numeric precision/nonfinite handling,
  missing data, timeouts, cancellation, failure codes, output limits and
  persistent-state rules. Initial example is stateless.
- Provider and controller restrictions are manifest requirements; assignment
  checks them against actual connection capabilities and selected wells.
  Unknown device type must not silently satisfy a controller restriction.
- Runtime dependency versions and contract versions are explicit. No runtime
  Internet installation on an on-premises node.
- A malformed result or failed input check fails the run visibly. It does not
  become an empty successful recommendation.

Exact Python function signature, wire schema and supported Python version remain
to be specified and tested before publishing the kit to services engineers.

## Local and live testing

Offline runner validates the contract and executes the same module entry point
against fixtures, without AutoPilot or ForeSite. Include valid, missing, stale,
bad-quality and wrong-unit cases.

Live runner uses an authenticated AutoPilot test API, configured with AutoPilot
address, existing connection ID and local AssetId. AutoPilot resolves external
identity and uses its adapter to collect declared inputs; the local runner feeds
them to the same calculation contract. Do not upload arbitrary code simply to
preview inputs. Do not send ForeSite credentials or arbitrary upstream URLs.
That API is proposed, not an existing endpoint.

Require server-side test/read permission plus exact asset/connection scope,
bounded requirements and request sizes, timeouts, audit and sanitized failures.
Use the established authentication architecture; prove a supported Windows
client path rather than assuming a Python HTTP client already handles it.
Recheck scope at execution; possession of a well ID does not authorize access.

A standalone live ForeSite runner is deferred. If required later, reuse shared
provider client code instead of copying vendor API logic into templates.

## Package import and assignment

Import performs structural, manifest, runtime/dependency and package-integrity
checks without executing package code. It does not certify algorithm correctness.
Preserve immutable version/hash identity and the existing approved-module trust
boundary. Admin-controlled import must not silently mark arbitrary bytes trusted.

Assignment selects exact module version, connection, reviewed wells, mappings,
parameters and Shadow mode. Compatibility is evaluated here. A saved view is a
selection aid; later view changes must not silently expand an active assignment.
Device/well-type changes use the asset lifecycle impact review.
Local tests do not establish deployment compatibility or trust.

The first runtime package loader needs explicit isolation, bounded resources,
dependency handling and a way to prevent direct network/control access. A Python
subprocess alone is not a security sandbox. Do not claim Shadow prevents writes
while module code can independently issue provider HTTP requests.

## ForeSite milestones and acceptance

1. Specify Python contract/template and deterministic offline runner using one
   observed input. Test contract failures and the same entry point/output shape.
2. Implement and verify one ForeSite input operation with recorded response
   fixtures and scoped bindings. Retain existing Gateway identity/session logic.
   Obtain sanitized real responses and confirm time/quality/unit semantics.
3. Add scoped live input preview and prove it on the actual ForeSite Windows
   installation. Distinguish CI fixture evidence from owner-run live evidence.
4. Deliver approved Python package execution, dependency packaging and isolation;
   reuse existing distribution decisions only after their prerequisites pass.
5. Assign one Lufkin SAM well and run manually in Shadow. Persist input
   observations, result, module version, failures and correlation. Prove zero
   provider writes, including no optimization-save call.
6. Expand to a reviewed multiwell selection, then scheduling. Cover partial
   well failure, overlap, cancellation and recovery through existing mechanisms.
7. Add a genuine deterministic algorithm. Governed recommendation publication,
   optimization-save and setpoint dispatch are later write slices with confirmed
   operation semantics and readback; they are not hidden in the Shadow milestone.

The ForeSite read-only milestone is complete only when the kit's authored module
runs through AutoPilot against a real selected well and produces inspectable
persisted results. Offline success alone does not satisfy end-to-end acceptance.

## CygNet continuation after ForeSite

Read together:
- ../architecture/cygnet-wami-integration-research.md (older research)
- ../architecture/logic-association-after-connections-research.md on this branch
- poc/connections-205: docs/poc/connections-205-findings.md

The same-host assumption remains the intended direct integration deployment.
The POC used CygNet.API.* .NET assemblies through a separate local agent,
not a proven COM-based integration. It reports compiling its net10 agent against
9.9 assemblies and cites ForeSite's net10 wrapper as supporting evidence.
That supersedes the older blanket assumption that a net48 agent is mandatory,
but it does not establish native-library compatibility on every installation.

The POC tested facility discovery through recorded responses. It explicitly
did not prove native Cx*64 DLL loading or a real facility read on a CygNet host.
The report says that test box had ODBC support rather than a full CygNet
installation. Do not generalize that limitation to the owner's current machine.

Resume with a narrow probe on the actual CygNet host: confirm installed API/native
files, versions and process bitness; resolve one facility; read one current
point with time/quality/units; repeat under the intended service identity.
Use installed help and supported API contracts. Record the exact failure if
loading, permissions or calls fail. COM is not selected merely because it was
discussed earlier. Agent transport, service identity and per-user attribution
remain explicit decisions. No Bridge requirement or transport switch is implied.

Reuse the authoring contract once CygNet inputs are proven; keep provider code
in its adapter/agent. Do not delay the ForeSite path on CygNet investigation.

## Open decisions before implementation

- Python entry-point/wire contract, version, dependencies and isolation.
- Exact first ForeSite input and whether existing AUC configuration is required.
- Confirm target server response fixtures and supported quality/time semantics.
- Approved package loading/distribution path and separate trust enforcement.
- Live test API authentication and exact scope enforcement.
- Which side owns later optimization publication/approval/dispatch.
- Actual CygNet-host probe evidence and identity model, deferred after ForeSite.

This list is a review baseline, not a claim that all implementation gaps are
eliminated. Update the findings as each milestone supplies concrete evidence.
