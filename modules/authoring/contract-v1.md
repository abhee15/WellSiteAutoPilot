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

This module requires `PumpFillage`, `%`, Good quality and a finite numeric value
between 0 and 100 inclusive. The input is at most 60 seconds old at evaluation,
with up to 30 seconds future skew, matching the current worker's freshness
policy. Equality at either time boundary is allowed. Missing source/time/quality
is rejected; adapter code must not replace it with request time or Good quality.
The 0–100 bound is this example's rule, not a generic SDK input restriction.

Quality/quantity matching is case-insensitive, requirement/role identity exact.
Dates must explicitly carry UTC (`Z` or `+00:00`). Unit conversion is a host
responsibility; fractions are rejected rather than silently multiplied by 100.
Malformed context and malformed JSON fail with exit code 2. Semantic failures
produce a result with a non-success outcome and no engineering result.

.NET SDK values are decimal. The Python starter uses JSON numbers and Python
int/float, so this profile is limited to simple observation/finite percentage
inputs. It is **not** an arbitrary-precision cross-language numeric guarantee.
The shared 72.5 fixture and boundaries are exactly representable. Before
shipping optimization algorithms needing precision/rounding, specify and test
a decimal wire profile and rounding policy instead of assuming float parity.

## Result and observations

Both runtimes return the SDK shape: outcomeCode, engineeringResults,
recommendations, controlIntents and stateUpdates. This stateless read example
always returns empty recommendations, controlIntents and stateUpdates.
Successful results contain one observed-value with local assetId, quantity,
value and unit. `WELL_INPUT_CHECK_COMPLETED` is the only successful outcome.

The test report retains the complete original context beside the result.
Timestamp, source and quality remain input observations; the SDK's
EngineeringResultValue has no timestamp/quality fields. Do not disguise a log
or diagnostic as persistent module state. The current SDK has no dedicated
reason/log collection: adding one is an explicit future contract change.

Named outcomes cover identity/binding/context errors, stale/future inputs,
rejected quality, wrong quantity/units, invalid value and missing source.
Production execution must surface failed outcomes as failures, not simply mark
any normally returned result successful. Mapping module outcome semantics to
production lifecycle states is a prerequisite for runtime integration.

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
