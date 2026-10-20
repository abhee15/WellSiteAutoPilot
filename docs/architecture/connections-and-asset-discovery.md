# Connections, templates, and asset discovery

Date: 2026-10-19; refined 2026-10-20.
Status: design discussion captured for refinement; not an implementation claim or a replacement for accepted ADRs.
Source: product-owner brainstorming on adapter selection, ForeSite discovery, hierarchy setup, and subsequent logic configuration.

## Existing baseline and verified gaps

Reviewed against develop commit `59e062ab4a6a3362d6ba28fe3a84610134744923`.

- [Provider adapter contracts](provider-adapter-contracts.md) already describe provider-neutral capabilities and compatibility.
- `src/integration/WellSiteAutoPilot.Integration.Contracts/Providers/ProviderContracts.cs` already defines `IAssetDiscoveryProvider` and `DiscoveredAsset` with external identity, parent external identity, provider type, and metadata.
- The Integration Gateway currently registers the simulator current-data provider. These contracts do not establish a working ForeSite/CygNet discovery adapter, connection-management workflow, or import implementation.
- The current domain Asset has an internal ID, type, name, parent ID, JSON attribute values, active state, and creation time. A durable external-source binding model needs explicit design; do not confuse it with configured-logic asset bindings.
- [Architecture baseline](architecture-baseline.md) puts application state in PostgreSQL and provider credentials in the Gateway.
- [UX plan](../design/ux-plan.md) proposes a later Data sources page; the relationship to the Connections entry point below remains to be resolved.

## Direction supported by this discussion

Start with connection setup and guided asset discovery, then build toward logic configuration. Keep the core provider-neutral and put product-specific service knowledge in adapters. ForeSite, CygNet, WAMI, and future providers may expose different capability subsets.

An ACL asset is a local representation of an external asset, with selected metadata and references back to its source. It also anchors ACL-owned configuration, authorization, logic assignments, and history. Import does not replicate the provider's complete database or transfer ownership of operational data to ACL.

The owner accepted connection-driven starter templates as a starting point to refine. Detailed screens, storage schemas, synchronization policy, and adapter contracts below are recommendations or open questions, not fully accepted implementation specifications.

## Concepts to keep separate

| Concept | Responsibility |
| --- | --- |
| Adapter | Installed backend code that translates an existing supported provider API into semantic platform capabilities |
| Connection | Named configuration using an adapter: endpoint, identity/secret reference, compatibility and operational settings |
| Asset type | Reusable ACL schema describing an asset's attributes |
| Hierarchy/grouping | Local organization and parent relationships; distinguish organizational categories from physical equipment |
| Source binding | Association between an ACL asset and a connection plus stable external identifier |
| Discovery mapping | Rules for selecting source objects, transforming metadata, assigning ACL types, and choosing destination parents |
| Logic data binding | Later mapping of a module's semantic inputs/outputs to authorized provider capabilities |

One adapter may serve multiple configured connections. Types should be reused where compatible rather than duplicated whenever a new connection is added. An asset may eventually have bindings to multiple providers; never merge assets merely because display names match.

## Proposed fresh-install experience

After database and access setup:

1. An authorized administrator opens **Administration > Connections** through the existing menu. This is a proposed entry point, not a new primary navigation requirement.
2. Select an installed adapter and add a named connection, for example ForeSite Production.
3. Configure its endpoint and supported authentication settings. The backend tests connectivity, authentication, compatibility, and permission for the intended operation.
4. Choose **Set up assets / Discover assets**. The adapter offers supported starter templates and available source classifications.
5. Choose an existing destination root or create an authorized root. Review proposed asset types, reusing compatible types and explicitly creating missing ones.
6. Choose hierarchy/grouping and mapping rules, source filters, and assets to include.
7. Preview additions, mappings, validation errors, and any conflicts before applying.
8. Apply the approved import and report confirmed outcomes and any failures. Import alone must not activate logic or authorize control.

Also consider **Discover into this node** from the asset tree, opening the same workflow with its destination already selected.

Connections are runtime configuration, independent of both provider installation and the AutoPilot installer. ForeSite, CygNet, and WAMI may be installed before or after AutoPilot and upgraded on their own schedules. Adapter code must be available before a connection can use it, but provider selection and connection creation must not require rerunning the AutoPilot installer. Independent adapter packaging remains a separate future design concern.

## Templates and user-defined destination hierarchies

Offer three distinct template concerns:

- Asset-type templates: schemas and display metadata.
- Hierarchy templates: organizational levels and parent construction.
- Discovery mappings: source fields/categories, filters, destination types, and source identity.

A customer can start from a template or an existing ACL hierarchy. For the owner's example, the proposed destination is ForeSite system, then well-type grouping, then device-type grouping, then matching wells. This is an example of the desired ACL organization, not a verified ForeSite API hierarchy.

A discovery rule can target a chosen node and reference a connection plus source classification filters. Explicit configured rules may combine parent settings; node names alone must not determine API behavior. If source records are flat but expose classifications, mapping can construct local grouping levels. If the service does not expose required information, show an integration gap or request explicit customer mapping.

Whether groups use existing typed Assets or a distinct grouping concept must be resolved against the current parent-based model. Avoid adding a general graph framework before a concrete requirement needs it.

## ForeSite adapter investigation

Owner-supplied facts: ForeSite exposes WCF operations and uses Windows authentication/NTLM. Endpoint transport, binding, security settings, actual operations, classifications, and relationships have not been inspected in this discussion.

Before committing to a discovery implementation:

1. Obtain the supported service contract or WSDL/MEX output and binding/security settings without committing customer endpoints or secrets.
2. Validate a representative read using the intended backend Windows identity.
3. Identify operations for wells, classifications, device types, relationships, stable identifiers, and paging where available.
4. Retrieve representative records in a test environment and distinguish source relationships from derived ACL grouping.
5. Map these results into the existing provider-neutral boundary; extend it only for demonstrated requirements.

The adapter must know which WCF operations to call. Creating an ACL type cannot teach the adapter an unknown service contract. Do not assume WCF metadata itself provides asset inventory.

Browser-to-ACL authentication and Gateway-to-ForeSite authentication are separate. Use an explicitly supported backend identity; do not assume browser identity can be forwarded through NTLM. This does not settle the separate CygNet end-user attribution/delegation requirement in [integration research](cygnet-wami-integration-research.md). Final identity selection and protected credential handling need deployment validation.

## Persistence, ownership, and repeat discovery

PostgreSQL remains authoritative for ACL assets, hierarchy, connection configuration, mappings, and source associations. JSON is appropriate for bounded flexible metadata and template definitions, not an assumption that all application state becomes loose JSON files. Reuse [asset attribute validation](asset-attribute-schema-v1.md).

Provider secrets remain behind the Gateway boundary; the browser and modules must not receive stored secrets. Decide whether configuration stores protected secret references or uses service process identity. Never store credentials in asset attributes or template exports.

Recommended synchronization behavior, to finalize during implementation:

- Match by connection plus stable external identity, with resource kind included where needed for uniqueness.
- Make repeated discovery/import avoid duplicates.
- Track metadata provenance and distinguish source-owned fields from ACL-owned fields.
- Preview changes to hierarchy and existing fields; preserve local logic and configuration.
- Flag missing source objects for review rather than automatically deleting assets.
- Validate authorization for destinations and mutations on the server; a preview is not authorization to apply later.
- Treat hierarchy moves as authorization-impacting changes.
- Keep imports auditable and report partial or uncertain outcomes accurately.

For overlapping sources, a designated hierarchy source per imported subtree is an initial simplification to evaluate. Other connections can enrich explicit bindings without silently competing over parent relationships.

## Boundary with logic configuration

Discovery answers which assets exist and how ACL represents them. Subsequent configured logic selects assets and binds semantic inputs and outputs to supported adapters. Selected metadata alone does not supply current measurements, historical data, calculations, write permissions, or command verification.

Modules remain provider-neutral and do not call provider services directly. ControlIntent and governed ControlAction retain the existing architecture boundary. New configurations default to Shadow; importing assets never enables autonomous execution.

## Follow-up scope and unresolved decisions

Preserve the active roadmap priority; this discussion does not instruct immediate adapter implementation.

First bounded adapter work: inspect a real service contract, prove connection/authentication and representative discovery, then specify the smallest complete connection-to-preview-to-import slice with appropriate authorization and idempotency verification.

Resolve before implementation:
- Connections versus the proposed Data sources navigation.
- First provider to implement and availability of a supported test interface.
- Grouping representation and source/destination hierarchy mapping.
- Stable binding identity and provider ID versus connection ID.
- Capability discovery, filtering, bounded results/paging, and import contract shape.
- Secret storage and service identities per deployment.
- Template compatibility/reuse and source-versus-local metadata ownership.
- Manual refresh first versus scheduled discovery later; conflict and missing-object policy.

No schema migration, frontend screen, installed provider support, or automatic synchronization is delivered by this note.

## Continuation: adapter implementation, templates, and writes

The following records the remainder of the October 19 discussion. It refines the earlier proposals without declaring uninspected ForeSite operations available.

### Initial provider focus and client dependencies

The owner's tentative sequence is to focus on ForeSite first, then consider CygNet and WAMI. Validate initial ForeSite work against 5.7.1. This sets an integration investigation direction; it does not interrupt the active development priority or authorize an assumed set of provider APIs.

Use one adapter per provider product/API family, supporting multiple named connections. Distinguish provider release, service-contract compatibility, and our adapter version. Share an implementation across compatible releases; introduce an internal version-specific client/mapping only when actual contract differences require it. Record validated support rather than assuming every patch release is compatible.

An adapter contains custom provider-specific code; authentication boilerplate alone does not implement discovery. It needs the service contracts and DTOs for the chosen operations and their dependencies. Evaluate generating a WCF client from supported metadata first; compatible shared contract assemblies remain an option after runtime/deployment review. Generated clients still depend on the wire contract. Referencing provider assemblies creates a build/deployment dependency that must stay inside the adapter, never in the core, frontend, or logic modules. No dynamic plugin loader is required for the initial implementation.

ForeSite's approximately 1,300–1,400 APIs are an owner-provided estimate, not a scope to wrap in full. Select only the operations needed for the first capability.

### Gateway and worker boundary

The Gateway hosts/routes to adapters. .NET and Python workers request authorized reads/calculations through internal Gateway contracts; they do not load ForeSite adapters or reference provider DTOs. Server-driven discovery also goes through the Gateway, with preview/import managed by ACL. Provider credentials remain within the integration boundary.

The existing contracts and simulator are groundwork. Real ForeSite, CygNet, and WAMI adapters were not registered in the reviewed Gateway implementation.

### Configurable discovery arrangements

The owner clarified that users should be able to choose arrangements such as:
- Wells only.
- Device-type grouping followed by wells.
- ForeSite system, well-type grouping, device-type grouping, then wells.

The adapter may internally retrieve wells, well types, device types, and other relevant classifications through distinct operations. The generic ACL-facing contract should expose supported discovery options and their relationships without hardcoding ForeSite operations into the UI. Exact contract shape remains to be designed.

Separate classification lists are insufficient: the adapter must provide the associations needed to place each well. A template selects grouping and mapping rules. Well type and device type may be independent classifications; their nesting can be a local presentation choice rather than a source-owned parent relationship. Offer only arrangements supported by available associations and handle absent/ambiguous classification explicitly.

**Discover builds the proposed tree; Apply persists it after review.** Discovery must not silently save the hierarchy. Review includes selection, filtering, conflicts, and validation. Repeated imports use source identity to avoid duplicates.

Prove one adapter's discovery using minimal connection configuration before building the full Connections UI. This validates the capabilities that the UI will present.

### Explicit write capabilities

Fetch/discovery and write operations are separate capabilities. Avoid an unrestricted set-any-property API. Distinguish:
- External metadata/configuration edits, with explicit validation, permissions, and audit.
- Operational/control settings, which follow ControlIntent, server policy/mode/authorization/approval/ownership checks, durable ControlAction, Gateway execution, and verification.

The adapter maps an explicitly supported action to the appropriate ForeSite operation. Inspect whether a Set operation changes stored configuration, queues a command, or changes equipment. Service acceptance is not proof of physical application; report the actual outcome and readback where supported. Timeouts can leave an uncertain outcome; do not blindly retry writes without provider-supported idempotency or reconciliation.

The owner reports that ForeSite can communicate with CygNet. This is not proof that a particular ForeSite Set operation reaches equipment or preserves required attribution. Validate the actual path. Unsupported writes remain unsupported; never automatically route a failed ForeSite action to CygNet. A CygNet path requires explicit binding, capability, authorization, and governance.

The first ForeSite scope can remain read-only well discovery. Measurements, calculation, and validated writes can be added when needed by configured logic.

### Public product research versus release-specific evidence

Weatherford public materials describe ForeSite as a production optimization platform combining surveillance/analytics and physics-based models, with artificial-lift optimization, failure prediction, workover planning, and reservoir/wellbore/surface-network optimization. These descriptions establish product scope, not the 5.7.1 WCF API, authentication settings, well classifications, or exposed relationships.

References:
- [ForeSite production optimization platform](https://www.weatherford.com/production-and-intervention/production-4-0/production-optimization-platform/)
- [ForeSite platform brochure](https://www.weatherford.com/documents/brochure/products-and-services/software/foresite-production-optimization-platform/)

Next evidence needed: supported 5.7.1 service interfaces/DTOs or WSDL/MEX, binding/authentication configuration, then representative test responses and relationships. The owner offered to share those details later. Do not infer a complete discovery hierarchy from marketing material.

## Continuation: connection scope and Windows service identities

### Connection feature scope

ForeSite, CygNet, and WAMI are installed independently of ACL. A connection identifies an existing provider instance; it does not install that product or grant access in it.

Keep the first Connections feature focused on adapter/provider selection, a friendly name, endpoint/server address, authentication mode, service identity or protected credential reference where needed, and test status. Templates, hierarchy mappings, and discovery rules reference connections but remain separate configuration.

The owner identified Connections as the next intended focus. A simulator-backed implementation can establish the foundation while real ForeSite contract details are pending. This is the latest direction for planning; it does not mean a Connections feature has been implemented.

### Administrator versus application identity

The ACL bootstrap administrator is an application authorization/recovery principal. It is separate from the Windows service logon identity. Installing as a user does not automatically make that user the runtime identity, and ACL administration does not grant external ForeSite permissions.

Review of `tools/WellSiteAutoPilot.Bootstrapper/Program.cs` on develop during this discussion found:
- Server: `NT AUTHORITY\NetworkService`.
- Integration Gateway: `NT AUTHORITY\LocalService`.
- .NET worker and NATS host: `NT AUTHORITY\LocalService`.

These are repository installer defaults, not an inspection of the owner's installed services. The Gateway makes outbound provider calls, so changing only the Server identity does not establish provider access.

NetworkService presents the computer's credentials remotely. On a domain-joined machine this normally identifies the domain machine account, such as `DOMAIN\ACLHOST$`; it is not a hidden named user/service account. LocalService normally presents anonymous network credentials.

References:
- [Microsoft: NetworkService account](https://learn.microsoft.com/en-us/windows/win32/services/networkservice-account)
- [Microsoft: Local accounts / service identities](https://learn.microsoft.com/en-us/windows/security/identity-protection/access-control/local-accounts)

### Options to evaluate, not a final identity decision

The owner asked whether NetworkService alone is sufficient. It can be sufficient for the Gateway when the environment supports machine-account authentication and the provider accepts and grants the machine account the necessary permissions. It does not automatically authorize access. Verify with a representative ForeSite WCF call from the running Gateway identity.

If ForeSite requires a named domain service account, deployment administration must configure an IT-provisioned identity with the required external rights, service-logon prerequisites, and local file/certificate access. Connection setup must explain which identity actually makes the call and report access failures. This is separate from the Connections feature and does not propose an installer change. Do not automatically provision domain accounts or reuse the ACL administrator.

Connections should be able to use the Gateway process identity without storing a separate provider password when that is supported. Alternative per-connection credentials require explicit adapter support and protected storage. Connection tests must execute through the Gateway under the configured runtime identity, not accidentally under the installing or signed-in user.

No identity switch or installer code change is authorized by recording this discussion. NetworkService versus a dedicated domain identity remains subject to the provider contract, customer permissions, deployment policy, and testing.


## Refinement: runtime connections, compatibility evidence, and external adapters

The October 20 direction supersedes the earlier installer-selection and installer-identity proposals. The owner requested this documentation update, not implementation of a plugin platform. The owner reports that ForeSite and WAMI do not offer versioned contracts; treat this as the working constraint until their interfaces are inspected. Do not require provider-side version negotiation or assume a product-version operation exists.

### Connections as independent configuration

Keep three lifecycles distinct: provider software installation, adapter implementation/deployment, and saved connection configuration. Co-location is a Phase 1 scenario, not a requirement; the same connection concept must work with remote providers.

AutoPilot owns the connection identity, authorized administration, endpoint/settings, protected credential references, adapter association, and validation history. Secrets stay within the integration boundary. A connection may use the executing adapter host's Windows identity when supported; selecting an authentication mode cannot change a Windows service account or grant provider permissions. For an external host, the outbound identity is that host's identity, not necessarily the Gateway's.

Local discovery can suggest installed providers/endpoints where a supported mechanism exists. Require confirmation and test the selected endpoint. Do not scan arbitrary networks, infer permissions from installation, or treat a local executable's file version as proof of a remote service's identity. Connections may be saved as unverified when a provider is not yet installed or reachable; only validated capabilities become eligible for use.

Present transport reachability, authentication/authorization, capability validation, and compatibility as separate results. A login success is not proof that discovery, calculation, or writes are supported. Show the actual execution identity where available, evidence source, time checked, and an actionable reason for failure without revealing secrets.

### Compatibility without provider-versioned contracts

Distinguish four independent versions: provider product release (possibly unknown), adapter package, AutoPilot adapter SDK/protocol, and semantic capability contracts. We can version the last three even when the provider exposes no version negotiation.

| Evidence | Use and limitation |
| --- | --- |
| Supported provider version/build operation, if one exists | Observed endpoint information; still requires an adapter support profile |
| Administrator-declared product release/profile | Recorded configuration with explicit provenance; not automatically detected or independently verified |
| Local installation/build information | Optional hint only; may not describe the selected service |
| Targeted non-mutating operation and response checks | Evidence for the exact exercised capability and identity; not proof of all API behavior |
| Maintained adapter support profile and representative integration tests | Documents validated contracts, mappings, units, semantics, known limitations, and supported deployment assumptions |

WSDL/MEX, where available, can help inspect the wire contract; unchanged metadata or a matching hash cannot prove unchanged behavior. Do not invent an API to enumerate all provider methods or dynamically generate a production adapter from unknown contracts.

Use a selected, validated adapter support profile together with bounded capability checks. Product release may remain unknown: that alone need not block a read capability with sufficient validated evidence. Conversely, an administrator-entered version, successful ping, or a broad declared version range must never promote an unvalidated capability to compatible. Preserve Unknown when evidence is insufficient.

Capability checks must be read-only, bounded, cancellable, and run using the actual configured backend identity. Do not test compatibility by sending equipment commands. Distinguish unavailable, unauthorized, inconclusive, and unsupported outcomes; a timeout or denied permission does not prove an operation is absent. Initially use explicit profiles and checks for the small set of implemented operations, rather than a general contract-negotiation engine.

### Provider upgrades and affected logic

Revalidate on explicit Test connection, connection edits, adapter/profile changes, runtime startup, and reconnect after an outage. Consider periodic checks later. Providers without version/change signals can upgrade silently; no probe schedule guarantees immediate detection. Offer an administrator maintenance/revalidate workflow for known provider upgrades.

Record evidence against the connection configuration revision, adapter/profile version, and capability set. Invalidate affected validation when those change, and prevent a stale in-flight test result from validating newer configuration. Validate configured logic when it is bound/activated and gate subsequent provider operations using current connection/capability state; cache freshness and command-time checks require a concrete policy before writes are enabled.

Modules normally declare semantic requirements (quantity, unit, quality, calculation/action and capability contract version), not ForeSite/WAMI release numbers. Provider-specific behavior belongs in the adapter. Existing module and binding contracts must be inspected before adding any requirement fields; this note does not claim that dependency-impact tracking exists today.

A provider upgrade need not require changing a module if the adapter preserves its required semantics. Revalidate affected configured-logic bindings when evidence changes; explain impacted assets, capabilities and configurations. Preserve saved assets, source bindings, history and logic configuration. Known incompatible or insufficiently validated required capabilities block dependent provider operations, especially control; unaffected capabilities can remain usable under policy. Health recovery alone must not restore invalidated compatibility or automatically resume suspended autonomous configurations. In-flight command outcomes require existing reconciliation/verification, never a blind retry against a new adapter.

### Third-party adapter development outside AutoPilot

Yes: a vendor or customer can develop an adapter in a separate repository against a published, provider-neutral contract. A generic adapter framework supplies the interface, lifecycle and tooling; each implementation still supplies product-specific authentication, calls, DTO translation and discovery relationships. It cannot learn an arbitrary third-party API from a connection URL.

Evolve the existing Integration.Contracts capability interfaces rather than introducing a competing abstraction. They are an initial semantic foundation, not yet a public SDK or externally hosted protocol. Before publishing an SDK, remove accidental implementation dependencies and define compatibility, lifecycle, connection context, paging, bounded errors, cancellation, correlation and conformance expectations. The current discovery signature accepts a continuation token but does not return a next token; address this when designing real paging rather than promising complete enumeration now.

An adapter descriptor/manifest should identify publisher, adapter ID/version, supported SDK/protocol versions, supported deployment/runtime dependencies, configuration-schema version, authentication options, implemented semantic capability versions, support profiles and optional templates. These are proposed metadata, not existing fields. Static advertised support and connection-specific validated/authorized capabilities are different. Identify the adapter type, host registration, connection and external asset separately; routing must not rely on display names or the current ambiguous provider ID.

Configuration schemas can describe validated fields, secret references and safe display hints so Connections can render standard forms without provider-specific frontend code. Migration of saved settings across schema versions must be explicit and reviewed; never silently discard credentials or mappings. External tools could manage connections through future supported, authorized AutoPilot APIs; they must not write directly to AutoPilot tables. AutoPilot remains the authority even when a vendor supplies setup tooling.

Two hosting choices remain possible:

| Hosting | Recommendation and tradeoff |
| --- | --- |
| Trusted .NET adapter in the Gateway | Smallest initial approach using explicit registration; adapter dependencies/runtime must fit the host, and deployment may require a controlled restart |
| Independently deployed adapter host/service | Preferred future option for third-party, incompatible-runtime or separately managed integrations; uses an explicitly versioned neutral wire protocol and authenticated Gateway registration |

A shared C# interface is sufficient for separately developed compatible .NET code, but it is not a language-neutral network protocol. An external host could use another language or a provider-required runtime after an explicit protocol is specified. Keep workers and modules calling the Gateway; the Gateway routes to either hosting form. External hosting moves provider-specific dependencies and outbound credentials into the approved adapter host, while preserving the integration boundary and existing governed action path.

An external host adds authentication, process supervision, credential provisioning, transport security, registration, timeouts, health and deployment work. Process separation alone is not a security sandbox; constrain host identities and accessible resources. Do not implement both hosting forms speculatively in Phase 1 or promise hot-loading/upload of arbitrary DLLs.

Adapter trust is distinct from logic-module trust. Approve publishers/packages or external host registrations administratively, verify package integrity/identity as applicable, and limit accessible secrets and capabilities. Signing identifies a publisher; it does not establish safety or compatibility. In-process third-party code shares the Gateway's privileges. Microsoft's [.NET plugin guidance](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support) explicitly notes that untrusted code cannot safely be loaded into a trusted process; AssemblyLoadContext dependency isolation is not a security boundary. Remote registration must also bind authenticated host identity to approved adapter/capability claims and authorized connections.

A published adapter SDK should eventually include a simulator/sample adapter and conformance tests for stable IDs, mappings, units/quality, paging, authorization failures, malformed responses, cancellation, timeouts, compatibility reporting, correlation, and supported command idempotency/readback. Passing these tests is required evidence, not a guarantee that provider upgrades are harmless. Provider access permissions remain external prerequisites.

### Recommended bounded sequence

1. Establish saved Connections and validation against the simulator, using a descriptor/configuration schema small enough for the concrete adapters.
2. Inspect the supplied ForeSite 5.7.1 contracts and prove a representative read/discovery with an explicit support profile and real runtime identity.
3. Validate the boundary with that first real adapter; then extract and publish a minimal adapter SDK/sample for independent development.
4. Add external hosting when a concrete third-party/runtime requirement needs it, with an explicit protocol and conformance checks.

This preserves an extensible direction while deferring a marketplace, arbitrary plugin loader and external hosting machinery. Exact SDK/protocol packaging, trust distribution, capability freshness policy, identity provisioning, settings migrations and command-time compatibility rules remain open for implementation design. The review/merge hold for PR #205 still applies.

### Review and merge hold

The owner explicitly requested that documentation PR **#205 remain unmerged** while they review and think further. This overrides the earlier automatic-merge-after-checks plan for this PR. Passing CI alone is not permission to merge #205; wait for a subsequent explicit user instruction. Keep additional discussion updates on the same documentation branch.
