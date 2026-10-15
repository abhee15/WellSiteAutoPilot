# Asset attributes: offline V1 schema contract

## Scope and dialect

Asset Types carry an immutable v1 schema string. Server-side Asset creation uses
**NJsonSchema 11.6.1** (MIT license; pinned in central NuGet package versions)
to evaluate values. To guarantee provider-neutral offline execution and avoid
network access, WellSite AutoPilot deliberately supports a **bounded subset**
of JSON Schema, not a claim to full 2020-12 support.

Schemas are JSON objects. A top-level schema can be `{}` (no constraints on
attributes) or declare `"type": "object"`; Asset attribute values must always
be JSON objects. Supported keywords, at the top level and nested properties:

- `type` as one of `object`, `array`, `string`, `number`, `integer`,
  `boolean`, `null` (at root, `object` only).
- `properties`: at most 64 named child schemas per object, to depth 6;
  `required`: up to 64 names;
  `additionalProperties`: boolean true or false.
- `items`: one child schema for an array; `minItems` and `maxItems`
  (0–1024).
- `minLength` and `maxLength` (0–1024); `minimum` and `maximum`;
  `enum` (1–64 allowed values).
- Display-only `title` and `description` (each up to 512 characters).

**All other keywords are rejected**, including `$schema`, `$ref`,
`$dynamicRef`, `$id`, remote or filesystem references, regex patterns,
`oneOf`, `anyOf`, `allOf`, `format`, `if/then/else` and custom vocabularies.
No resolver is configured for external locations; the entire schema is checked
before it reaches the library. Schema size is limited to 32 KiB UTF-8, values
to 64 KiB, and JSON parser depth to 32. Unknown constraints must never be
silently skipped. Existing database schemas are revalidated before creating
assets; malformed legacy types are denied until remediated.

The server returns stable errors:
`ASSET_ATTRIBUTE_SCHEMA_UNSUPPORTED` (unsupported/unsafe schema),
`ASSET_ATTRIBUTE_SCHEMA_INVALID` (malformed schema),
`ASSET_ATTRIBUTE_VALUES_INVALID` (values malformed/nonobject/not valid
against type).

## Example

```json
{
  "type": "object",
  "properties": {
    "field": { "type": "string", "minLength": 2 },
    "depth": { "type": "number", "minimum": 0 }
  },
  "required": ["field"],
  "additionalProperties": false
}
```

Valid values: `{"field":"Alpha","depth":1200.5}`.

The first operator feature intentionally provides a schema-guided JSON editor,
**not** a universal auto-generated field builder. Unsupported schema constructs
are not advertised as valid. A future field editor can be layered over the same
contract with its own UX/compatibility tests.

## Access and writes

`GET /api/v1/assets/capabilities` provides presentation-only hints for root
and authorized parent creation. It does not authorize writes; `POST /assets`
independently requires `assets.manage`, a built-in administrator for root
creation, or Asset scope for an existing parent. This follows current legacy
runtime policy; the dynamic role/grant authorization cutover (#79) is separate.

After `201`, the SPA verifies the server response and uses scoped
`GET /assets/{id}` before announcing selection. The list remains based on
server-authoritative paging. When POST response is lost, the UI reports
**uncertainty** rather than automatically replaying the write. Operators should
refresh and inspect the Asset list before considering another create.

## Library choice

NJsonSchema 11.6.1 is published under MIT and used offline. Newer JsonSchema.Net
packages were **not** chosen because their upstream binary EULA introduces a
commercial maintenance fee; adding such a package requires separate licensing
approval. Revisit schema dialect support as a versioned API/Asset Type contract,
not as an implicit behavior change in the parser.
