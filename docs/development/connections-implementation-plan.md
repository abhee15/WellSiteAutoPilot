# Connections: high-level implementation plan

Updated: 2026-10-20.
Status: owner-requested plan for the agreed simple-first direction; no implementation completion claim.
Design: [Connections, templates, and asset discovery](../architecture/connections-and-asset-discovery.md).

## Outcome and order

After authentication is completed and validated, deliver a small Connections feature, prove one real read-only adapter, then build discovery and logic usage on that foundation. Keep adapters in separate projects within the AutoPilot repository and release tested adapters with AutoPilot initially. Preserve the existing Server/Gateway/worker topology.

The steps below are cohesive delivery slices, not mandatory one-commit or one-PR quotas. Keep frontend, backend and meaningful validation together. Avoid a long backend-only buildout and avoid splitting trivial edits into repeated CI cycles.

| Step | Deliverable | Completion evidence |
| --- | --- | --- |
| 0. Finish authentication | Complete the active Windows/browser/auth validation; record the actual runtime/deployment identity prerequisites | Existing auth acceptance and relevant current-head CI pass; no connection feature bypasses the unresolved gate |
| 1. Connection management and authorized simulator use | Saved named connections, explicit adapter registration, Administration UI, test/status, basic edits/disable and server/Gateway enforcement | A user can create/test/reload a connection through the UI and use the simulator only through authorized bindings; denials and stale saves are tested |
| 2. First real adapter: ForeSite | Inspect supplied 5.7.1 contracts, implement the smallest read-only client and test operation under the actual backend identity | Representative real-service read succeeds in the intended environment; supported settings, operations and limitations are documented |
| 3. Discover, review and apply Assets | Durable source bindings, bounded discovery, a small starter mapping/template, preview and explicit import | A selected real well set imports correctly; repeat Apply avoids duplicates, conflicts are visible and destination/Asset authorization is preserved |
| 4. Use connections in configured logic | Select persisted source bindings for the implemented input capabilities; route an authorized Shadow execution through the Gateway | One suitable approved module runs end to end with real inputs and explicit units/quality/error behavior; disable or denied access prevents further use |
| 5. Add capabilities/providers when needed | Implement the next concrete ForeSite read/calculation, or CygNet/WAMI adapter, using the proven contract | Provider-specific tests and a representative environment check demonstrate the requested capability without leaking provider types into core/modules |

Steps 1 and 2 can use supplied contract investigation in parallel with development as appropriate, but real provider access must not precede the authorized Gateway boundary. If the ForeSite contracts/environment are unavailable, complete the simulator slice and report that specific blocker; do not fabricate provider compatibility or let a mock stand in for real-service proof.

## Step 1: smallest complete Connections feature

1. Confirm existing permission, audit, transport, persistence and UI components to reuse. Resolve the existing Connections/Data sources naming under Administration.
2. Define the minimal connection record: stable ID, name, adapter key, typed supported endpoint/auth settings, enabled flag, revision and audit fields. Keep last-test evidence tied to the tested revision. Separate protected secret references only if needed.
3. Persist through the existing EF/migrator path. Apply the repository's disposable development database policy where relevant; no production startup migration or elaborate development data conversion.
4. Register the simulator adapter explicitly and extend only the existing semantic contracts needed for named connection routing and a bounded read-only test. Support two connections to the same adapter without conflating their settings or failure state.
5. Add privileged list/detail/create/edit/test/disable APIs and matching generated frontend client. Protect connection configuration server-side; normal execution use retains separate Asset/operation authorization.
6. Authenticate internal Gateway callers and enforce permitted Asset/connection/operation resolution. Do not expose an arbitrary external-ID data proxy. Keep provider secrets within the integration boundary.
7. Build the Administration screen alongside these APIs: select an available adapter, name/configure/save, test, show last result/time, edit or disable. Use existing tokens/components and generated transport types; no generic form engine.
8. Reject stale writes and stale test completions. Block deletion when referenced and endpoint/adapter replacement once bound; new connection plus explicit rebinding handles source replacement initially. Audit successful configuration changes without secrets.
9. Validate the complete saved-connection and authorized-use journey before merging the feature.

Keep startup/test calls and normal reads bounded. Disabling blocks new calls at the dispatch check; it is not a provider shutdown or a guarantee that an already sent request was canceled. A lightweight reference query is sufficient initially; no graph engine, command-drain orchestrator or retired-state framework is required.

### Validation for this slice

- Real PostgreSQL/API/UI persistence, reload and stale-edit conflict.
- Unauthorized administration, direct Gateway access and cross-Asset/cross-connection substitution denied.
- Actual configured backend identity used for tests; no secret disclosure in responses, audit or logs.
- Late test result cannot validate changed settings; missing adapter/credentials and timeouts produce useful results.
- Two connections to one adapter route independently; an unused unavailable connection does not globally block unrelated work.
- Disabled/referenced connection behavior, restart and audit consistency.
- Relevant frontend, provider/Gateway and integration tests plus repository CI on the final reviewed head. No real Windows/provider validation claim from mocks.

## Step 2: ForeSite evidence and read-only adapter

Obtain supported contracts/DTOs or metadata, bindings/auth settings and a test environment. Identify only the operations needed to test the connection and retrieve wells/classifications/relationships. Choose a generated WCF client or compatible assemblies based on actual runtime/dependency evidence.

Keep the implementation in its own adapter project and publish a small tested-support note. Do not require a provider version endpoint: use known tested configuration/profile plus the representative read. Unknown release and incompatible operation are different outcomes. Provide manual Test connection after a known provider upgrade; no automatic upgrade scanner or general negotiation system.

Validate under the running backend identity, first interactively as a diagnostic if useful and then in the actual service context. Do not silently rely on the installer user's credentials. This step may expose missing external permissions or unavailable API data; document the gap instead of widening scope.

## Steps 3 and 4: discovery and logic are separate slices

Start discovery with one useful mapping, such as wells under a selected root, adding type-based grouping only when the inspected data supports it. Adapter results need stable IDs, relationships/classifications and real paging. Preserve selected local metadata ownership and exact-Asset visibility. Discover previews; Apply reauthorizes and persists idempotently.

For logic usage, inspect the current manifest and binding model before adding fields. Resolve connection/source bindings from persisted authorized configuration, preserve execution/correlation provenance and test an actual Shadow path. Add current-data or calculation operations only when the selected module needs them; discovery alone does not supply measurements.

Before operational writes, separately resolve CygNet per-person attribution, scheduled/autonomous execution identity, governing permissions/readiness, action semantics and reconciliation/readback. These do not block a clearly limited read-only Connections feature, but cannot be omitted when writes are introduced.

## Maintenance and deferred work

Maintain support notes, recorded-response/contract tests and representative integration tests per adapter. Build and ship tested adapters with AutoPilot; provider products retain their own installation/upgrade cycles. Update support claims only with evidence, and retain basic manual validation after provider changes.

Do not make these prerequisites: a marketplace, public SDK, independently released adapter packages, dynamic plugin loading, universal JSON configuration schemas, automatic source-identity detection, automatic compatibility graphs, multi-version negotiation or remote adapter hosting. Revisit a minimal SDK after real adapter experience and a concrete third-party requirement. A required provider-specific agent can be designed narrowly if runtime constraints prove it necessary.

## Review and merge discipline

This plan does not start implementation or override active authentication work. PR #205 remains draft and unmerged until explicit owner approval. Later implementation follows feature branches and reviewed PRs to develop, required authorization/regression checks and current-head CI. Each PR should leave a usable, validated slice and document any real-environment prerequisite still outstanding.
