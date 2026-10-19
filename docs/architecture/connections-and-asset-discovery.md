# Connections, templates, and asset discovery

Date: 2026-10-19.
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

Installer selection can determine available adapter components. Connection creation and discovery belong in the app so connection changes do not require reinstalling the product. Packaging, optional dependencies, and later component installation remain open.

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

Browser-to-ACL authentication and Gateway-to-ForeSite authentication are separate. A dedicated service identity is the initial recommendation; do not assume browser identity can be forwarded through NTLM. This does not settle the separate CygNet end-user attribution/delegation requirement in [integration research](cygnet-wami-integration-research.md). Final identity selection and protected credential handling need deployment validation.

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
