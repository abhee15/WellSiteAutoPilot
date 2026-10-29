# Structured ForeSite surface-card reads

This extends the existing saved-connection/session/read-plan path for #302. The
`poc/cards-e2e` branch is evidence only; its runtime gates and local Aspire changes
are not merged. Scalar V1/V2 execution and scalar wire values retain their shapes.

## Contract and execution

EngineeringValue carries exactly one scalar Value or a StructuredValue containing
schema identity and JSON data. Structured values use null for the scalar slot,
never a numeric placeholder. The V3 immutable input snapshot includes reviewed
capability/schema identities. The first live capability is `structured.current`
for ForeSite `SurfaceCards` with schema `foresite.surface-cards/v1`; source routing
still comes exclusively from the persisted read-plan reference and current grants.
No new registry, credential route or module-accessible provider client is added.

The package host requires an exact V2 manifest/interface pair, rejects dual V1/V2
entry points before construction and preserves hash/manifest/identity checks.
The worker compares input policy/capability/schema identities with the reviewed
manifest and reuses StructuredModuleOfflineHost's schema/context/output validator.
Successful structured observations and results remain JSON values in the existing
OutputJson envelope, marked valueContractVersion 2. The execution detail view
provides bounded, escaped inspection of both. Failure diagnostics retain bounded
metadata and typed codes, not arbitrary failed provider payloads or module outputs.

Initial inline limits reuse the schema validator's 64 KiB value ceiling; the whole
read reply is limited to 256 KiB UTF-8 and the saved result envelope to 512 KiB.
The existing 256 KiB evaluation envelope and schema-depth constraints also apply.
HTTP card responses are read incrementally and stop at the byte ceiling, even
without a Content-Length header. Oversized collections fail closed; no automatic truncation, chunking or Object
Store payload indirection is introduced. Larger/downhole use cases need measured
limits and a separately reviewed transport extension. Deploy Server, Gateway and
worker together: older scalar workers cannot consume this additive capability.

## ForeSite mapping and unresolved quality gate

The reader uses GET Dynacards/Dynacard/LatestWithUnits/Lib/{well}/{type}, with types
1 CURRENT and 2 FULL, through the shared cookie/401 session and saved virtual path.
Every SurfaceCards entry is retained, up to 16 cards and 1024 paired points per
card. Arrays remain ordered and may differ in length across cards. The library
response timestamp is retained per card and labelled CardCapture; retrieval time
is separate and cannot refresh it. The oldest capture bounds collection age and
future capture times are rejected. Response-derived IDs include kind, timestamp
and array index; they are not claimed to be native persistent ForeSite card IDs.
Only the POC-observed in/lb units are accepted, preserved without assuming lb=lbf.
Unknown types, downhole string encodings and other units are not guessed.

**ForeSite provides no confirmed per-card quality code in the POC evidence.** The
reader reports Unknown, and the current reviewed input policy rejects Unknown.
Consequently actual library-card execution remains blocked with
EXECUTION_INPUT_QUALITY_REJECTED until a quality mapping or explicit reviewed
unknown-quality acceptance policy is agreed. This change deliberately does not
turn a successful library GET into Good sensor/acquisition quality. A successful
synthetic Good-quality integration fixture is not live quality evidence.

Local acceptance must verify response timestamps apply to every returned surface
entry, unit meaning, multi-card identity, missing-card responses and quality policy.
The 2022 POC cards are archived evidence, not proof of freshness for control.
Provider writes, scans, scheduled live authority and Python hosting remain disabled.

## Verification

Adapter regressions cover multiple ragged cards, exact values/units/times, session
cookie/path/GET isolation, malformed points/types, duplicate fields, invalid times,
unsupported units, upstream error text and oversized responses. Existing scalar
session and measurement regressions remain in place. The hosted integration suite
adds a synthetic authorized structured read larger than the old 8 KiB threshold,
actual approved package execution, schema validation, calculated card arrays and
PostgreSQL evidence persistence through NATS. An Unknown-quality fixture proves
failed input diagnostics persist without card payloads or module results. The
read-plan authorization suite also runs with structured inputs, including identical
well IDs on two connections, grant revocation and source/revision changes. UI tests inspect escaped structured
values without numeric coercion. CI does not access a live ForeSite installation.
