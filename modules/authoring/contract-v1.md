# Starter module contract, version 1

This is a narrow, executable authoring profile of the existing product SDK,
not a replacement SDK or a general-purpose Python execution protocol.

## Boundary and identity

The fixture envelope has integer `contractVersion: 1` and `context`. The
context uses the camel-case field names of
`src/modules/WellSiteAutoPilot.ModuleSdk/LogicModuleContracts.cs`. All fields in
the supplied fixtures are required. JSON null is not a missing-data substitute.
UUIDs identify local AutoPilot assets/executions; they are not ForeSite well IDs.

| Context field | Meaning |
| --- | --- |
| executionId | Nonempty UUID for this invocation |
| moduleIdentity | Exact moduleId and version of this runtime's manifest |
| correlationId | Nonblank string retained in the test report |
| evaluationTimeUtc | Explicit UTC ISO 8601 timestamp used for age checks |
| parametersJson | JSON **string** containing an object, matching the SDK |
| assets | Exactly one well role with local assetId and object-valued parameterOverridesJson string |
| inputs | Exactly one pump-fillage input for that local asset |

Each input includes requirementId, assetId, quantity, value, unit,
timestampUtc, quality and source. No provider object or HTTP client crosses
this boundary. The host resolves selected inputs before invoking code.

## Starter validation and numeric profile

The calculation requires `PumpFillage`, `%` and a finite value between 0 and 100.
Quality and freshness belong to the host's reviewed manifest policy. The shipped
manifests require Good quality and maximumAgeSeconds 60; a revised reviewed
manifest can change maximum age or permit Uncertain without another hidden
60-second/Good-only rule in the algorithm. Unknown and Bad remain rejected.
The production .NET worker and trusted offline .NET runner share one validator.
The Python offline host implements the same policy and cross-runtime tests cover
changed age/quality requirements. Python production hosting remains unavailable.

The host allows up to 30 seconds future skew. Equality at either time boundary
is allowed. Missing source/time/quality is rejected; never substitute request time
or Good quality. The 0–100 bound is the example's algorithm rule.

Quality/quantity matching is case-insensitive, requirement/role identity exact.
Dates must explicitly carry UTC (`Z` or `+00:00`). Unit conversion is a host
responsibility; fractions are rejected rather than silently multiplied by 100.
Malformed context and malformed JSON fail with exit code 2. Semantic failures
produce a result with an explicit failureCode and no engineering result.

.NET SDK values are decimal. The Python starter uses JSON numbers and Python
int/float, so this profile is limited to simple observation/finite percentage
inputs. It is **not** an arbitrary-precision cross-language numeric guarantee.
The shared 72.5 fixture and boundaries are exactly representable. Before
shipping optimization algorithms needing precision/rounding, specify and test
a decimal wire profile and rounding policy instead of assuming float parity.

## Result and observations

Both runtimes return the SDK shape: outcomeCode, engineeringResults,
recommendations, controlIntents, stateUpdates and optional failureCode (SDK 1.1).
This stateless read example
always returns empty recommendations, controlIntents and stateUpdates.
Successful results contain one observed-value with local assetId, quantity,
value and unit. This example uses `WELL_INPUT_CHECK_COMPLETED` on success.
Hosts use failureCode, not the spelling of outcomeCode, to determine failure.

The test report retains the complete original context beside the result.
Timestamp, source and quality remain input observations; the SDK's
EngineeringResultValue has no timestamp/quality fields. Do not disguise a log
or diagnostic as persistent module state. The current SDK has no dedicated
reason/log collection: adding one is an explicit future contract change.

Named outcomes cover identity/binding/context errors, stale/future inputs,
rejected quality, wrong quantity/units, invalid value and missing source.
Set failureCode to a nonempty stable code when evaluation fails; null or absent
means success. Codes start with an uppercase ASCII letter and contain only
uppercase ASCII letters, digits and underscores, at most 128 characters. Both
.NET execution paths reject failure results before completion or output handling
and use the existing ExecutionFailed path. Malformed codes/results fail as
MODULE_RESULT_INVALID. No recommendation, intent or state update from a failed
evaluation is applied. Failed production executions persist bounded input diagnostics through the existing
result history; offline reports retain the original context.

Existing SDK V1 modules that omit failureCode keep their prior success behavior.
An outcome such as NO_ACTION_REQUIRED can therefore be a valid success.
Authors must migrate any old failure-as-outcome convention explicitly. Kit
version 1.0.2 requires SDK/worker 1.1.0 or later; do not deploy it on a worker
that ignores the new property. This does not implement Python production hosting.

## Host limits and trust

Offline fixtures are bounded to 256 KiB. These hosts execute the supplied trusted
example only; they are not sandboxed code runners. Arbitrary import must not
execute code. A future live/package host must enforce permissions, resource and
output bounds, deadlines, cancellation, dependency availability, package
version/hash identity and isolation before enabling customer code.

.NET exposes the existing cancellation token and the example checks it.
The Python example is a bounded synchronous calculation; it has no new
cancellation protocol or persistent state. Python process termination, timeout
and worker-message handling remain future host responsibilities. Shadow labels
alone cannot prevent arbitrary code from issuing direct network requests.

## Offline reviewed policy

Both runners accept an optional second argument, a reviewed manifest path. Without
it they use their shipped manifest (copied alongside the built .NET runner).
Use this to exercise changed policy; a changed deployment still needs a new
immutable package version and approval. These trusted offline runners invoke
only the shipped calculation and admit its outputs after policy validation.
Production validates acquired inputs before invoking package code.

The 1.0.2 starters remove duplicate age/quality policy from the calculation.
Their SDK minimum remains 1.1.0. Repackage as 1.0.2; never replace 1.0.1 bytes.
