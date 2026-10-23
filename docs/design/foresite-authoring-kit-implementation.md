# ForeSite authoring kit implementation and live gates

Base reviewed: develop `58c83990b001528adde24d816dfef0b7e82ae6eb` (2026-10-22).
This slice implements offline authoring only. The preceding proposal is on
`docs/logic-association-research-205` in
`docs/design/foresite-first-module-authoring-plan.md`; it is not assumed merged.

## Delivered in this feature

- A .NET ILogicModuleV1 example and a Python equivalent, separate immutable IDs.
- Product-format manifests restricted to ForeSite and one well/current input.
- Versioned JSON fixture convention based on the existing SDK, deterministic
  offline hosts and reports retaining source/unit/time/quality/correlation.
- Successful, missing, stale, bad-quality, wrong-unit, future-time, cross-well,
  value-boundary and malformed-fixture cases; cross-runtime CI on Windows/Linux.
- Packaging instructions reusing the existing CLI and validator. No new
  authoring UI, credential store, provider client or package loader.

## Repository gaps confirmed

The .NET resolver registers only weatherford.sample-shadow-read. Python main.py
only starts/waits. ForeSite advertises AssetDiscovery, not CurrentData. Gateway
current-read routing and mapping need connection/source-aware extension before
these authored modules can read ForeSite through AutoPilot. Registering a
manifest in the catalog is not executable package installation.

SourceBinding already stores ConnectionId, ExternalId, ResourceKind and local
AssetId. Resolve the saved external well ID there; do not add another hidden
ID or search by display name. Modules receive normalized local-asset inputs,
not a vendor identity they can independently route.

## Reference script evidence

Reviewed the owner's `RRL_OverNetwork 2.py` and `RRLPoweShellScript 2.ps1`.
Neither is directly compatible with this kit. They combine authentication,
provider reads, calculations and writes; the Python test uses random values.
No customer address, script payload or credentials are copied into this kit.

| Operation | Script evidence | Next action |
| --- | --- | --- |
| Session | GET TokenService/CreateToken | Reuse existing adapter cookie/Windows identity and one-401 retry |
| Well identity/details | GET Wells/Well/{wellId} | Validate target response and selected saved binding |
| AUC configuration | POST WellAutoSetPointService/GetAutonomousControlConfiguration with WellId array | Confirm whether this requires existing AUC logic; avoid duplicate setup dependency |
| Current setpoint metadata | GET Surveillance/Well/SetPointsParams; PowerShell references AllSetPointParams | Confirm polling effects, exact DTO, units, source time/quality and error semantics |
| Optimization publication | POST WellAutoSetPointService/SaveAutonomousControlOptimizationData | Excluded from Shadow; side effects require a later explicit write design |
| Device dispatch | POST Surveillance/SendSetPointsParams/true | Excluded from this kit and first live slice |

These are evidence of calls in scripts, not live validation of those endpoints.
Neither script establishes a PumpFillage telemetry operation with timestamp and
quality. Do not invent an endpoint, extract a similarly named setpoint as
telemetry, or stamp unknown values as fresh/Good. No live server was exercised.

## Next implementation order

1. Obtain a sanitized response for one supported numeric current measurement
   on a known ForeSite well, including its units, original timestamp, quality,
   mapping key and missing/denied cases. Pump fillage is preferred only if
   supported; otherwise choose a verified measurement and revise both templates.
2. Implement that named read in the ForeSite adapter behind existing contracts
   and session behavior. Test recorded responses, errors, missing provenance,
   unit/quality handling and no cross-connection fallback.
3. Connect authorized input collection to saved source/connection revisions.
   Recheck asset and connection scope, enabled state and mapping at execution.
   A preview must not accept arbitrary upstream URLs or treat an external well
   ID as authorization. Reuse established caller authentication/audit.
4. Add a scoped read-only preview for the local kit through AutoPilot, proving
   a Windows-authenticated client path. Configuration contains AutoPilot URL,
   selected connection and local Asset IDs; ForeSite credentials remain in the
   Gateway. No arbitrary code upload is needed for input preview.
5. Host approved authored modules. Define Python execution/cancellation,
   package/dependency loading, isolation and outcome-to-lifecycle failure
   handling. The existing .NET engine validates inputs but accepts any module
   result; the input-check failure outcomes must not be recorded as success.
6. Assign one reviewed well from a Lufkin SAM view in manual Shadow, confirm
   compatibility against real capabilities, persist observations/results and
   prove zero provider writes. Keep selected wells frozen if the view changes.
   This is the first live end-to-end acceptance, requiring owner-run ForeSite
   testing. Offline tests alone do not satisfy it.

Preserve existing package approval and immutable hash/version trust rules.
Admin import is not automatic trust. Customer-authored execution and resource
isolation need their own evidence before enabling them. PowerShell is a future
runtime option using the same semantic boundary; do not add an enum/runtime
until its host, contract and deployment are implemented. CygNet remains a
separate discussion/implementation stream.

## Verification limits

Python fixture/CLI tests run locally. This authoring environment has no .NET
SDK; the dedicated CI job compiles the template against the real SDK, validates
the manifests with the existing CLI and runs parity cases on Windows/Linux.
Check current-head CI before treating the .NET template as verified. Installer,
database, API authorization and production worker behavior are unchanged.
