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

The first adapter quantity is ForeSite SurfaceCards. Unknown library-card quality
currently fails closed, pending a reviewed quality policy. Python, downhole cards,
large-payload indirection and other structured provider capabilities remain future
work. The offline authoring host continues to support synthetic arbitrary shapes;
its validation is reused by the production worker.
