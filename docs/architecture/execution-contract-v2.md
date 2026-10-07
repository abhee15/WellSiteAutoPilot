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
