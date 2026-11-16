# Connections, templates, and asset discovery

Updated: 2026-10-20.
Status: owner-agreed simple-first direction; detailed implementation choices remain subject to evidence and review. No connection feature, real provider adapter, public SDK or control support is claimed by this document.

## Agreed direction and scope

Connections is the next foundational area after the authentication work is completed and validated. Start with a usable configuration feature and one real adapter. Keep provider-specific complexity behind the existing integration contracts, and extend those contracts only when the first implementation demonstrates a need.

Maintain each provider adapter in its own project in the AutoPilot repository, with its own provider dependencies, mappings and tests. Initially build, test and release supported adapters together with AutoPilot. ForeSite, CygNet and WAMI themselves remain independently installed products, before or after AutoPilot, with their own upgrade cycles. They do not need AutoPilot-specific modifications.

Connection creation and edits are runtime configuration: no provider-selection installer workflow, installer account changes, independent adapter release system or general plugin platform is part of this feature.

See the [high-level implementation plan](../development/connections-implementation-plan.md) for delivery order and completion criteria. This direction supersedes the earlier broad plugin and installer proposals, rather than making every discussed option a Phase 1 requirement.

## Responsibilities and deployment

| Part | Responsibility |
| --- | --- |
| AutoPilot core | Assets, hierarchy, grants, configured logic, execution, licensing and control policy |
| Connections | Named configuration referencing a supported adapter and an external instance |
| Integration contracts | Provider-neutral discovery, data, calculation and governed-action semantics |
| Provider adapter | Product-specific authentication, API calls, DTO translation and supported mappings |
| Integration Gateway | Explicit adapter/connection routing and authorized provider calls |

These are responsibility boundaries, not five new processes. Retain the existing Server, Gateway and worker architecture. The Server manages saved configuration in PostgreSQL; provider credentials remain within the integration boundary. Workers and modules use the Gateway rather than provider APIs or assemblies directly. Governed writes retain the Server policy and durable ControlAction boundary.

A separate provider agent/process is justified only by a demonstrated runtime, bitness or vendor-library constraint, such as the direct CygNet investigation. That is a bounded integration decision, not a general external-host framework.

## Customer setup

1. Install AutoPilot and finish authentication/access setup.
2. Make the provider service available and arrange its required backend identity/permissions.
3. Open Administration > Connections using the existing menu.
4. Choose an available adapter, give the connection a name, enter supported endpoint/auth settings, save and test it.
5. Discover Assets, review the proposed hierarchy, and explicitly apply the import.
6. Configure logic against those Assets and the adapter capabilities actually supported.

Steps 5 and 6 follow the first Connections slice; saving a connection does not import Assets or activate logic. Connections versus the older proposed Data sources navigation should be reconciled without adding a new primary navigation item.

Co-location is an initial deployment scenario, not a requirement. Manual endpoint configuration must work for local and remote providers. Optional local endpoint suggestions can follow only when a supported discovery mechanism exists; they do not determine compatibility or permissions.

## Minimal connection configuration and behavior

The first implementation needs a stable connection ID, display name, adapter key, endpoint/settings supported by that adapter, enabled flag, configuration revision and audit metadata. Show last test time and an actionable result separately from administrative enabled state. A disabled connection is not the same as an unavailable provider.

Start with explicit typed settings for the simulator and first real adapter. Small shared validation/form components are useful; a generic schema-driven form engine, settings-migration framework and adapter manifest format are not prerequisites.

Use the backend process identity where sufficient. Support a protected credential reference only when a concrete adapter needs it; do not build a general credential manager pre-emptively. Never return stored secrets to the frontend or modules, or include them in Assets, templates, logs or exports. Endpoint/auth settings cannot change the Windows service logon account or grant external access.

Tests run through the Gateway under the actual backend identity, use bounded non-mutating operations, and report reachability, authentication/authorization and the exercised capability accurately. A successful ping is not proof that discovery or writes work. Keep permission errors, unavailability, unsupported operations and inconclusive checks distinguishable without a large status workflow.

### Essential safeguards

- Initially restrict configuration, testing and discovery to protected administrators through explicit server policies. Define the small set of necessary product permissions during implementation; ordinary authorized logic use does not imply connection administration.
- Authenticate internal callers and authorize the Asset, selected connection and operation. Resolve provider bindings server-side; the service account's provider rights do not become the user's AutoPilot rights.
- Reject stale saves using the existing concurrency/audit conventions. A late test result must not validate a newer configuration revision.
- Allow harmless renames. Once a connection is referenced, reject endpoint/adapter replacement in the initial implementation; a source move/replacement uses a new connection and explicit rebinding. This avoids building an automatic source-identity detector.
- Block deletion when references exist; disable instead and preserve history. Check enabled state at dispatch, bound in-flight calls by timeout, and explain that disabling cannot recall an already sent operation. Add command drain/reconciliation policy before enabling writes.
- Do not automatically fail over, retry uncertain writes, grant access to imported children, or enable autonomous execution.
- Use supported transports and certificate validation where applicable. Keep timeouts/results bounded, preserve correlation IDs and redact diagnostic output.

The reviewed Gateway currently has a simulator router and internal data routes without authentication/authorization middleware. It is groundwork, not a ready production connection-use boundary. Close and negatively test that boundary before real provider access.

## Adapter implementation and maintenance

Use the existing [semantic provider contracts](provider-adapter-contracts.md), not a second provider abstraction. One adapter per product/API family can serve multiple named connections. ForeSite, CygNet and WAMI need not implement the same capabilities. The adapter registry is explicit code registration initially, not dynamic assembly scanning.

For each adapter maintain a short support note: tested provider releases/configurations, selected operations, auth/binding prerequisites, known limits and representative test coverage. Ship provider dependencies according to their runtime and redistribution constraints; do not assume all provider assemblies can load in .NET 10.

Keep provider contract/DTO dependencies inside the adapter. Generated WCF clients and compatible shared contract assemblies are alternatives to assess with real interfaces; neither eliminates wire-contract coupling. Do not wrap all of ForeSite's owner-estimated 1,300–1,400 APIs. Begin with the operations needed for well discovery.

## Compatibility and provider upgrades

The owner reports that ForeSite and WAMI do not offer versioned contracts; use that as the working constraint until interfaces are inspected. Do not assume a provider version endpoint or negotiation exists.

Initially document tested support profiles, such as ForeSite 5.7.1, and run targeted read-only checks. Show a detected product version only when a supported operation supplies it; label administrator-declared information as configured, and otherwise show unknown. Unknown product release alone need not block a read validated against a supported contract/profile. A configured version or successful ping cannot establish untested compatibility.

After a known provider upgrade, the administrator retests the connection before resuming affected use. Relevant configuration/adapter changes invalidate the previous test. Perform bounded validation at startup/reconnect for capabilities the adapter supports. No claim of automatic upgrade detection or exhaustive compatibility checking is made; a provider can change silently.

Modules consume semantic capabilities and engineering values; they normally do not depend on ForeSite/WAMI release numbers. Preserve validated units, quality and behavior at the adapter boundary. Detect unsupported required operations with clear errors and prevent their use. Do not build a general compatibility/dependency graph, automatic module migration, background version scanner or multi-version negotiation engine for the first feature.

## Asset discovery and ownership

Discovery is the next cohesive slice after saved connections and real read-only validation.

Keep asset types, local hierarchy, source bindings and discovery mappings distinct. An ACL Asset stores selected metadata, a reference to the source, and ACL-owned configuration; it does not copy the provider's whole database. PostgreSQL remains authoritative for AutoPilot state.

Adapters return stable external identities, required classifications and their associations. A template chooses the destination arrangement, such as wells only, device type > wells, or ForeSite system > well type > device type > wells. These are desired local groupings, not verified ForeSite API relationships. Offer only arrangements supported by real data; node names cannot teach the adapter new API operations.

Discover proposes a tree; Apply persists the authorized reviewed selection. Reuse compatible asset types, keep repeated imports idempotent by connection plus stable external identity/resource kind, and preview conflicts rather than overwriting local fields or deleting missing objects. Do not match Assets merely by display name. Resolve paging explicitly; the current discovery interface accepts a continuation token but does not return a next token.

Imports must preserve exact-Asset grant semantics and explain visibility of new Assets. Existing configured-logic Asset bindings are not the durable external-source binding model. Later measurement/calculation/action bindings are separate from hierarchy import.

## Identity prerequisites and write scope

The bootstrap administrator, signed-in person and backend Windows identity serve different purposes. The reviewed installer defaults are Server = NetworkService, Gateway/worker/NATS = LocalService; these are code defaults, not a claim about the installed machine. Connection testing must establish what the running integration component can actually do. Customer deployment administration handles any required identity changes without making installer changes part of Connections.

NetworkService can use a machine identity remotely when supported and authorized; it does not guarantee ForeSite access. Do not assume browser NTLM identity can be forwarded through queued work. Validate the real WCF binding and intended identity.

The existing [CygNet user-attribution requirement](cygnet-wami-integration-research.md#1b-cygnet-must-know-who-the-user-is-owner-2026-10-19) remains unresolved: determine whether external enforcement of each person's rights or supported on-behalf-of attribution is required, and which identity scheduled/autonomous actions use. A service-account connection does not settle this.

Initial ForeSite work is read-only. Writes are a separate later capability: inspect what each operation actually changes, retain Server policy/mode/authorization/approval, durable actions, idempotency/reconciliation and readback where supported. Service acceptance is not proof of equipment change. ForeSite communicating with CygNet does not prove a particular write reaches equipment; no automatic alternate-provider fallback.

## Future extension options, not Phase 1 commitments

The boundary should allow separately developed adapters eventually. Independent development, deployment and release management are separate decisions.

After real adapters prove the contracts, consider a minimal public SDK/sample and conformance tests if a concrete third-party integration needs them. A vendor still implements its own product-specific calls; a generic interface cannot infer an arbitrary API. Keep AutoPilot authoritative for connection configuration.

External adapter hosting, language-neutral wire protocols, publisher/package trust distribution, arbitrary plugin uploads/hot loading, schema-driven settings, independent adapter updates, export/restore tooling and automatic compatibility impact analysis are deferred. A separately hosted adapter requires explicit authentication, authorization, credential and runtime-lifecycle design. Do not preload this machinery into the first Connections feature.

## Baseline and evidence needed

Reviewed against develop baseline `59e062ab4a6a3362d6ba28fe3a84610134744923` and PR #205's documentation branch. Existing code includes provider contracts, current-data simulator registration and the Asset model; it does not establish real ForeSite/CygNet/WAMI connections or discovery/import support. The current runtime entitlement plan supersedes legacy authorization descriptions in older setup notes.

Before ForeSite implementation, obtain the supported 5.7.1 interfaces/DTOs or WSDL/MEX if available, binding/auth settings and representative non-sensitive test responses for well IDs/names/classifications/relationships/paging. Validate these under the real backend identity. WCF metadata describes contracts, not an Asset inventory or proof of product compatibility.

Existing architecture, non-invasive provider boundaries, exact-Asset authorization and governed control remain applicable. Proposed ADR-009 readiness must be resolved before relying on it for control; Connections does not introduce a second readiness engine.

## Review and merge hold

PR #205 remains draft and unmerged for owner review. Passing CI is not permission to merge this PR; wait for an explicit owner instruction. Documentation approval does not itself implement or validate the described features.
