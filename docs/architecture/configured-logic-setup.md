# Configured Logic setup — operator slice B

The Logic page supports approved-module selection, explicit Asset roles and
provider input mappings, parameter/override JSON objects, optional ScheduledOneShot
cadences, immutable revision copies, validation and separately confirmed activation.
All configurations remain Shadow. An enabled cadence starts scheduled Shadow work
when activated; the editor defaults to no cadence. It does not send physical commands.

## Authority and validation

`GET /api/v1/configured-logic/capabilities` returns presentation-only `canManage`
from the current legacy permission claims, protected by `configured-logic.read`.
All mutations retain `configured-logic.manage` and the existing exact-Asset server
scope checks. Every historical revision must remain in scope to inspect or modify
a configuration. No parent/child scope inference or scoped-grant policy cutover is
introduced. Identity/role/scope changes remount the workspace; denied reads/writes
clear detail and editor data. Mutation responses never confer permissions.

Only enabled catalog versions appear in the editor. The server independently
compares the submitted manifest with the installed immutable version and checks
trust/enabled status during validation and activation. HTTP registration cannot
self-assert publisher trust. Operational catalog approval/provisioning remains a
separate prerequisite; this screen does not grant it.

Asset choices are the currently loaded active authorized inventory (bounded by the
existing 100-item page), filtered by declared Asset Type keys. Provider IDs, external
IDs and mapping objects come from the site's adapter configuration. V1 manifests
have no parameter JSON Schema; this screen explicitly describes structural object,
binding/cardinality, module-readiness and schedule checks, not semantic parameter or
live provider-connectivity validation. JSON objects remain raw in requests so that
duplicate keys reach authoritative server rejection. Parameters, overrides,
manifest and mapping JSON reject duplicate keys, depth over 32 and UTF-8 size over
64 KiB. No provider is contacted during setup validation. Live simulator execution
and result inspection belong to slice C.

## Revision consistency

Content is immutable. Changing it creates a new numbered Draft; validation transitions
only Draft to Validated. Activation accepts only Validated, checks module readiness
and active Asset bindings again, supersedes previous Active rows and updates the
active pointer in one transaction. A PostgreSQL definition-row lock orders revision
creation, validation and activation. A stale next revision number returns stable
`CONFIGURED_LOGIC_REVISION_CONFLICT` (409), rather than leaking unique violations.
Conditional updates prevent a delayed validation overwriting Active status.

The browser sends `If-Match: "<active-revision-guid>"` or `If-Match: "none"` with
activation, representing the active revision it reviewed. Mismatch returns 409.
The header is optional for existing API clients; those callers still receive the
repository's transactional comparison against the service's current snapshot.
There is no automatic conflict retry or implicit activation of the latest revision.

Each successful create/revise/validate/activate records an audit event in the same
transaction, with authenticated actor, correlation ID, configuration and revision
IDs. Raw parameters, provider mappings and manifests are excluded. Conflicts leave
no successful-mutation audit. No persistence migration, service topology or installer
change is required.

## Browser and verification boundaries

After writes, the UI checks a strict receipt plus a fresh authorized readback before
enabling the next lifecycle step. Lost responses, malformed receipts and failed
readbacks block repeat writes. The user reloads persisted revisions and reviews
current state; new-configuration uncertainty requires closing the editor and
refreshing/inspecting the list first. Cancellation aborts requests without claiming
the server rolled back a sent command. Errors show localized guidance and stable
codes, never arbitrary server detail. Shared semantic design tokens, native labels,
keyboard controls, flexible layouts and localization keys apply to every new field.

React DOM interaction tests cover creation, copying, scope/type filtering, empty
catalogs, invalid inputs, denial, stale activation, explicit confirmation, uncertainty
and unmount cancellation. Real HTTP tests use the isolated Asset API PostgreSQL
suite database and trusted offline module fixture, exercising middleware, scoped
creation, immutable history, conflicts, revocation and transactional actor/correlation
audits. Separate PostgreSQL-context races reproduce competing activation and stale
revision-number creation. This does not validate a real Windows browser Negotiate
handshake; the release gate remains #92.
