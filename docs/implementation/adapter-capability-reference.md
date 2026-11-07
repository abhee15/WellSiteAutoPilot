# Adapter capability reference

Tracking: #484. Supersedes the quantity-first Operations presentation in #485.

The screen describes code shipped in the installed adapters. Opening ForeSite requires neither a saved Connection nor
provider access. The page reads only `/api/v1/connections/adapter-operations`; the authorization policy remains
`connections.read`. It does not read Connection settings, browse Assets, acquire data, or execute commands.

## Declaration and extension

`AdapterDescriptor` owns both its typed operation descriptors and versioned `OperationGroups`. A group is a semantic
capability (purpose and referenced operation keys); an operation declares a callable contract; quantities and fields
are details inside that contract. This is one catalog, not another registry. The generic UI renders the groups the
server projects. Adding a group for a future provider does not require a provider-specific page or application switch.

Only major-version-1 groups with complete, unique references to supported read operations are projected. Discovery,
commands, undeclared operations, duplicate group keys and unsupported group versions cannot manufacture capabilities.
Adapter-owned documentation records an exact typed API call, its provider API/field mapping, execution boundary and synthetic JSON examples.
Conformance checks deserialize examples into actual request/result types, check required fields and preserve payload
shapes. The frontend also rejects malformed examples, duplicate identities and missing/unsupported group references.

## ForeSite initial scope

| Capability | Operation / supported data | Explicit exclusions |
| --- | --- | --- |
| Surveillance data | Current PumpFillage and AveragePumpFillage via the existing typed current provider | Full surveillance DTO, arbitrary fields, well-test-derived telemetry |
| Well configuration data | Well details: name, WellType classification code, FacilityId, Field, Lease and optional PrincipalDeviceType | Engineering model files, complete provider DTO passthrough, writes, well tests |
| Well trend data | Bounded raw PumpFillage history with UTC `[start,end)`, units, unknown source quality and truncation | Daily averages, arbitrary trends, hosted-module history binding |
| Card data | CURRENT/FULL surface cards, typed schema, paired arrays, capture times and source units | Downhole cards and unrestricted card payloads |

These are the implemented subset, not a promise to wrap the provider's entire API. Surveillance field variants share
one typed function and appear inside their capability. Well configuration is a distinct typed provider interface and
result; it is not discovery presented under another label.

## Well details boundary

`IAssetConfigurationProvider.ReadConfigurationAsync` receives an explicit source identity. The ForeSite implementation
uses authenticated GET `Wells/Well/{id}`, validates the returned Id, bounds the source body to 1 MiB (including chunked
responses), limits projected text fields and rejects malformed/duplicate fields or ambiguous timestamps. Unknown
provider properties never leave the adapter. Numeric identity/classification identifiers retain their exact text value.

`SourceChangedAtUtc` is the optional source ChangeDate normalized to UTC. Missing source time remains null.
`RetrievedAtUtc` is a separate retrieval fact. Configuration carries no invented telemetry quality or scan timestamp.
The Gateway configuration endpoint is ServerPolicy-only and reuses the existing revision-scoped cookie/Windows-auth
session, cancellation handling and six-second deadline. There is no public/browser configuration preview endpoint.

## Adapter contracts and module consumption

The API tab documents the typed adapter interface **inside the Integration Gateway**. Current scalar inputs and
reviewed V2 structured-card inputs can be supplied by the authorized runtime today. Adapter history/configuration
reads do not imply that corresponding hosted-module input bindings exist. The page states this distinction in its
overview and examples. A future module authoring workflow must consume the same versioned catalog and add governed
bindings; modules never gain direct provider/Gateway access or receive Connection settings/credentials.

CygNet publishes surveillance and only the history supported by its installed backend. Simulated history is identified;
native history activation remains separate from Sri’s local native probe. PetroVisor has no invented data reads, and
WAMI is not invented in the registry. Each provider can implement different capability families.

## Validation

Provider tests use source-shaped/synthetic fixtures; Gateway HTTP tests prove service-caller authorization and exact
provider routing against the simulator. Browser tests exercise the real capability UI with synthetic descriptors at
desktop, tablet and 320/390px phone widths. These checks are not live ForeSite/CygNet validation. Sri owns local
provider validation; installing licensed CygNet libraries in hosted CI is not a merge requirement.
