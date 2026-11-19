# Structured module manifests, version 2

Manifest version 2 makes the SDK 1.2 structured input/output contract reviewable
as part of a package. Version 1 retains its scalar behavior and rejects structured
declarations rather than ignoring them. V2 currently requires the .NET runtime
and minimumSdkVersion >= 1.2.0 (a 1.2.0 prerelease is insufficient).

Each data requirement retains its existing ID, asset role, quantity, access,
canonical unit, age and quality policy, and adds:

| Field | Meaning |
| --- | --- |
| `valueSchema.schemaId` | Module-owned exact identity, at most 128 characters; never fetched as a URL |
| `valueSchema.schema` | Inline supported JSON schema with an explicit root type |
| `requiredCapability` | Provider capability identifier, e.g. `structured.current`; a declaration, not routing authority |

The required `outputRequirements` array declares `id`, `quantity`, optional `unit`
and `valueSchema` for each accepted engineering output. An output declaration
specifies an allowed shape, not a requirement that every evaluation emit it.
Both input and output declarations are bounded to 1–64. IDs within each collection
are unique, case-insensitively. Reusing a schema ID with conflicting shapes is
rejected. Schema IDs themselves are exact, case-sensitive identities.

Schemas use the existing bounded, reference-free validator, including the wrapper
used by structured SDK validation. Unsupported constraints, external references,
duplicate fields and oversized schemas fail validation. No additional schema
interpreter or dependency is introduced. Schema object property names are
case-sensitive; DTO member names are case-insensitive. The existing package
manifest limit remains 1 MiB and each wrapped schema remains limited to 32 KiB.

The CLI/package reader and catalog registration validate all input/output schemas,
not just settings. Catalog and configuration revisions retain the complete
manifest JSON. Package hashing and exact reviewed-manifest comparison cover these
declarations without a new table, migration or package format. The example at
`modules/samples/structured-cards/manifest.json` is the single source of its
reviewed schemas. Its offline execution also checks declared role cardinality
and the optional parameter schema before invoking module code.

## Execution capability boundary

The bounded .NET structured path is described in
[ForeSite structured cards](../implementation/foresite-structured-cards.md).
V2 requires a matching V2 entry point; dual V1/V2 implementations remain rejected.
Reviewed capabilities and schema identities travel with immutable authorized read
plans. Catalog acceptance is not source authority or evidence of live quality.

The first adapter quantity is ForeSite SurfaceCards. The card library supplies no
quality, so the adapter labels a stored card `Uncertain` (as it does a daily value). It
is still refused by default: a module must declare `allowUncertainQuality` to use it, and
`Unknown` quality is rejected everywhere. This opt-in is provisional pending a reviewed
quality policy (live question F10 in the gaps branch). Python, downhole cards,
large-payload indirection and other structured provider capabilities remain future
work. The offline authoring host continues to support synthetic arbitrary shapes;
its validation is reused by the production worker.

## Scalar current inputs

A V2 input may declare a scalar current reading with the registry's own
`engineering.current` capability and a schema whose root type is `number`. The schema
identity is the module's own (`authoring.<input>.number/v1` for authored drafts), so it
does not name a provider. Assignment accepts it when the connection's read rule serves
that quantity and the declaration's root type is a number. At run time the worker reads
the value through the same authorized read plan as any current read, checks quantity,
unit, quality, age and the declared schema, and hands the module a typed value with
its unit. Cards keep their exact-match capability. Scalar history, daily-average and
well-test reads are not yet runtime capabilities.

## Scalar daily inputs

A V2 input can read one number from a provider's production-day record, such as yesterday's cycles or run time. It
declares `access: Current`, the provider's own field name as `quantity`, the capability `engineering.daily` and a number
schema. There is no registry list of these names: the provider's daily record decides which fields a well has, and
authoring offers the ones discovered for the selected asset.

- **Which day:** the latest production day that has finished, using the provider's own start and end. A day in progress
  is never read. The reading's timestamp is that day's end and its kind is `ProductionDayEnd`, so `maximumAgeSeconds`
  measures from the end of the day.
- **Quality:** the daily record carries no verified quality, so the reading is `Uncertain`. The input must set
  `allowUncertainQuality: true`.
- **Unit:** the provider's own unit for that field. A field with no unit fails the read.
- **Missing:** a field absent from the latest finished day fails with `FORESITE_MEASUREMENT_MISSING`. Zero is a value.
- **Scope:** ForeSite only. Other adapters refuse `engineering.daily`. Setpoints and well-test values are not covered yet.
