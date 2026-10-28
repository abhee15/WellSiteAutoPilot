# Structured card authoring example (offline only)

SDK 1.2 adds `ILogicModuleV2`, with schema-identified JSON values for inputs and
engineering results. V1's decimal constructors and interface are unchanged.
Numbers, strings, booleans, explicit nulls, vectors, matrices and nested records
can cross the V2 boundary without conversion to strings or dummy scalar values.
A schema ID identifies a reviewed shape; it is not a URL to fetch.

Run the executable example and its contract checks from the repository root:

```sh
dotnet run --project tests/WellSiteAutoPilot.ModuleSdkTests --configuration Release
```

`CardCalibrationModule.cs` consumes `cards.json` and a configuration record with
`loadScale: 2` and `loadOffset: 10`. It returns the two calibrated curves and
peak load **490 lbf**. The current card has five samples and the full card three;
the full card retains its older capture time. This is synthetic linear load
calibration, **not Gibbs/Everitt-Jennings analysis or a downhole-card solver**.
The schema deliberately restricts this example to surface cards in inches/lbf.
Those are algorithm requirements, not universal platform/card restrictions.

## Validation and shape

The trusted offline host takes a separate `StructuredModuleDefinition` containing
reviewed input/output schemas. Input values cannot supply their own schema or
relax the host's policy. All inputs are validated before invocation, and all
outputs before return. Inputs retain quantity, source, timestamp, quality and
optional scalar unit. Mixed-unit data declares units in its schema and payload;
there is no fabricated unit for an entire card collection.

The schema validator reuses the existing offline-safe Asset schema profile:
`type`, `properties`, `required`, `additionalProperties`, `items`, `minLength`,
`maxLength`, `minimum`, `maximum`, `minItems`, `maxItems`, `description`, `title`,
`enum`. Unsupported keywords, references and duplicate properties are rejected.
Every value requires an explicit root type. Type coercion is not supported.
A nested array schema with equal minItems/maxItems on each row enforces matrix
width; separate card records can have different lengths. Cross-channel length
alignment and card identity/timestamp semantics are checked by this module.

Limits are inherited from the shared profile: 32 KiB schema and 64 KiB value,
including the internal `value` wrapper; schema nesting at most six levels
including that wrapper, JSON depth 32, declared min/max lengths at most 1024,
64 properties per schema object. The offline host additionally allows at most
64 inputs, outputs and asset bindings and a 256 KiB context/result envelope.
These are bounded authoring limits, not measured production transport sizing.
Callers must bound external bytes **before** JSON parsing; these APIs consume
already materialized objects and are not a sandbox against hostile module code.

Raw JSON numeric text and decimal precision survive validation/serialization.
Nonfinite values and numbers outside the finite double range are rejected;
algorithms choose their own numeric conversion and range/rounding requirements.
This example's bounded decimal calibration does not imply cross-language
arbitrary-precision parity. There is no Python V2 implementation yet.

Observation freshness/quality uses the same policy as scalar execution. A
collection observation timestamp must not replace per-card capture timestamps.
For this stored-card example, the old FULL card is intentionally allowed;
algorithms needing fresh individual cards must validate each capture time.
`FailureCode` prevents any failed result from escaping the offline host. No
recommendation/control intent is executed by this host.

## Production boundary and remaining work (#302)

This is an executable SDK/authoring slice. It is **not deployable structured
execution**. The current package host rejects V2 (including dual V1/V2 entry
points) before construction, with `MODULE_CONTRACT_UNSUPPORTED`. Existing V1
modules remain supported. No catalog manifest, provider capability or live card
endpoint is added; no database or installer migration is required.

Next work must carry the reviewed schema/capability declarations through the
manifest and immutable authorized read plan, bounded provider/broker transport,
worker dispatch, result persistence and inspection/export. Only then can the
production host accept V2. Obtain the installed ForeSite card HTTP contract
before adding its adapter; field names remain the provider contract and CygNet
UDC mapping remains ForeSite's responsibility. Reading stored cards must not
implicitly issue a controller scan or other command.
