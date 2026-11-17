# Execution message contract V2

Execution request V1 represented one Asset and one current Quantity. That was sufficient for the first Shadow vertical slice but cannot represent normal Logic Modules with multiple Asset roles or multiple data requirements.

V1 remains unchanged for compatibility. New Configured Logic dispatch uses `wsa.execution.requested.v2`.

V2 snapshots the immutable execution inputs needed by a worker:

- Configured Logic and revision identity;
- exact Module ID and version;
- operating mode;
- parameter JSON;
- role-based Asset bindings;
- semantic data requirements and provider bindings;
- request timestamp.

Workers resolve each semantic input through the Integration Gateway and construct the provider-neutral Logic Module SDK context. The execution message contains provider mapping identifiers, but never provider credentials or CygNet/WAMI implementation types.

Current data quality and freshness are revalidated at execution time. Historical data is represented by the V2 contract but remains unsupported by the current .NET execution engine until the governed history-read slice is implemented.

Message-contract V1 is not mutated because messaging contract versions are independent and breaking contract changes require coexistence rather than in-place replacement.

## Hosted package requests (V3)

`wsa.execution.requested.v3` wraps the unchanged V2 execution context in
`ExecutionRequestedV3.Execution` and adds `Package` containing the catalog's
approved SHA-256 and normalized reviewed manifest JSON. The Server takes these
values from a locked approved/enabled catalog row in the same transaction that
persists the request and outbox. Clients cannot supply a checksum through the
configured-execution endpoint. Metadata-only/built-in dispatch retains V2.

The worker retrieves that single object, preflights its complete bytes against
the expected identity/runtime/actual host SDK and reviewed manifest, and releases
its module lease after evaluation. V1/V2 cannot fall back to hosted packages.
V3 changes request identity, not result semantics: it emits the existing V2
started/completed/failed messages. Reads and decisions use the same persisted
execution Asset scopes for all three request versions.

Retirement stops new dispatch; requests committed before retirement may finish
with their pinned archive. A missing or corrupt archive fails explicitly rather
than substituting another version or built-in. This does not establish current
provider connectivity, connection-aware input routing or arbitrary-code isolation.

## Optional structured calculation trace in completion evidence

For a successful hosted structured module, the trusted structured host may return an optional `CalculationTrace` validated against the approved input requirements and bounded scalar step schema. The worker adds it as `calculationTrace` **inside the existing V2 completed result's `OutputJson` only when present**. No new message subject, event version, control intent, or dispatch path is added. With no trace, the serialized structured result is byte-for-byte compatible with the previous shape.

This evidence is **module-reported, host shape-validated**, not proof that a provider reading or numerical algorithm is correct. Host validation precedes publication; failed evaluations still emit `ExecutionFailedV2`, not successful calculation evidence. The completed execution's existing identity, replay and permissions remain authoritative. The current authoring UI does not yet consume this field and must not invent intermediates where it is absent. The synthetic card calibration can produce a trace offline; its live hosted-provider acceptance remains pending.
