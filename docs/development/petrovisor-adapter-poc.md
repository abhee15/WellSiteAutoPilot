# PetroVisor adapter — POC research and implementation proposal

Status: research / design only; no runtime changes or live PetroVisor validation.
Date: 2026-10-20
Branch: `feature/poc-petrovisor-adapter`
Target: discussion before adapter implementation.

## Decision summary

Introduce PetroVisor as a workspace-scoped provider under the existing WellSite AutoPilot Connections and adapter architecture. Phase 1 reads **existing PetroVisor workspace data only**. Do not implement PetroVisor data connections, data sources, ingestion, acquisition, or data cleansing.

**Workspace is the mandatory parent and source boundary** in the AutoPilot navigation/import experience. User-proposed default display: `Workspace > Entity Type > Entities`. This is a *synthetic classification tree*, **not** necessarily PetroVisor's defined asset hierarchy. Preserve selectable PetroVisor named hierarchies as an alternative browse/import view: `Workspace > [Hierarchy] > actual Root > nested child entities`. Avoid silently imposing entity-type levels in source-defined hierarchical paths. Start with the simpler default, evaluate both views with real workspace examples, and adjust from testing and operator feedback.

Keep PetroVisor libraries and service objects inside `PetroVisor` adapter project(s); neutral contracts in AutoPilot. Reuse saved connections, Gateway authorization, source-binding identities, reviewed imports, asset types, audit, execution and approval controls. Do not construct another integration framework or independently distributed plugin scheme yet.

## Verified public resources (as of 2026-10-20)

- https://www.nuget.org/packages/MyrConn.PetroVisor.API/3.12.2609.1
- https://www.nuget.org/packages/MyrConn.PetroVisor.Web.Client/3.12.2609.1
- Both advertise version `3.12.2609.1` and .NET 10 targeting. Version is an initial investigation baseline, not proof of compatibility with deployed servers.
- API assembly provides public interfaces and domain objects. Web.Client provides `PetroVisorServiceProvider` and client REST proxies/service container. Public sample construction: `new PetroVisorServiceProvider(EndPointConfiguration.EUCloudDiscoveryLatest, "workspace", "user", "password")`. Sample is documentation, **not** a preferred production auth mechanism.
- API describes `RepositoryService.Get<Entity>()`, `GetWithList<Entity>()`, `EntityType`, `Hierarchy`, `Signal`, `Scope`, `DataRequest`, `DataServices.GetStaticNumeric()`, `GetTimeNumeric()`, `GetStaticString()`, `GetTimeString()`; service interfaces expose paging/asynchronous calls where applicable.
- An `Entity` has an `EntityType`; entity names are unique *within a workspace*. An entity may have an alias.
- A `Hierarchy` is an optional, named collection of child-parent pairs; documented methods `Root`, `GetChildren(Entity)`, `GetParent(Entity)`, `ValidationError`. An entity has one parent *within a particular hierarchy*, not necessarily across all workspace hierarchies. Time-based hierarchies and `IHierarchyRelationshipService` are documented in release history.
- Data request identity is Entity + Signal + Unit (unit optional; signal storage unit used by default). `TimeNumericData` has timestamped points and `StaticNumericData` has one value; null/empty numeric represented by `Double.NaN`. PetroVisor offers server-side unit conversion.
- Data services `Retrieve/Load`, `Save`, `Delete`, `Acquire` exist. Documentation says `Save` removes existing affected data first: **do not enable writes until semantics are experimentally verified**. We do not need `Acquire` in phase 1.
- P# scripts and `PSharpService` are separate optional future capabilities; scripts can save results as a side effect. Do not enable arbitrary P# execution as a read shortcut.

## PetroVisor-to-AutoPilot mapping

A saved Connection selects an endpoint/environment **and exactly one effective workspace** for this initial adapter. If an endpoint supports many workspaces, use separate explicitly named workspace-bound connections initially; do not rely on mutable ambient workspace selection.

Canonical source scope:
```text
provider = petrovisor
connectionId = <AutoPilot immutable saved-connection id>
workspaceKey = <canonical workspace id, or workspace name with documented rename limitations>
entityKey = <stable vendor ID if available; otherwise workspace-scoped canonical name>
entityType = <source entity type>
hierarchyKey = <selected named hierarchy when hierarchical view used>
```
Never key entities by display name alone without their connection and workspace. Entity name uniqueness is documented workspace-wide, but immutable entity identity/renaming behavior must be validated with the SDK and test server. Changing endpoint/workspace on a connection that already owns asset bindings must be blocked or handled as an explicit reviewed migration. Preserve existing referenced-connection deletion protection.

Two **presentation** modes over the same discovered canonical entity set:

1. **By entity type (suggested default)**
   ```text
   Workspace: Production
     Entity type: Field
       Field A
     Entity type: Pad
       Pad 01
     Entity type: Well
       Well 101
       Well 102
   ```
   Each type is a *virtual group*, not a source entity, nor a relationship asserting Field A owns Well 101. UI must label it `Browse by type`. Only actual Entities become bound assets; synthetic workspace/type nodes are navigation/grouping containers, not equipment or physical-control targets.

2. **By named PetroVisor hierarchy (source-truth alternative)**
   ```text
   Workspace: Production
     Hierarchy: Production Assets [selection / metadata]
       Field A [actual source Root]
         Pad 01
           Well 101
   ```
   Hierarchy selection is required when importing source-parent relationships. Hierarchy name is metadata/container, not necessarily a new physical asset. Root can be an entity of *any* entity type. Multiple hierarchies can place the same entity differently; import must not generate duplicates or overwrite user-maintained relationships without explicit review. Unrelated entities must remain discoverable outside the chosen hierarchy. For non-tree, invalid, time-varying or missing hierarchy, show warning, fallback type view, and require deliberate import options.

Avoid conflating **classifications** (entity types) with **relationships** (hierarchies). Model workspace as the top-level browse/namespace boundary; decide later whether workspace becomes a persisted Asset Group, a provider-source grouping, or UI-only organizational node based on existing asset-schema constraints and tests. Do not invent a new global `Workspace` asset type just to power a tree.

### Discovery/import contract expectations

- List and validate accessible workspace, type catalog, entities and their type/aliases, named hierarchies and validation issues.
- Choose one hierarchy for a *hierarchical* preview; show counts, missing parents, orphan entities, cycles/invalid relationships, type mapping conflicts, duplicate source bindings, already-associated assets, removed/renamed entities.
- Preview first, explicitly apply; do not perform silent destructive reparenting.
- Save source-bound identity, connection, workspace, entity, entity type, optional hierarchy provenance, import/review timestamp and revision/etag where available.
- Apply in durable transaction according to existing Connections import path. Use idempotent upsert semantics keyed by canonical binding; honor user-controlled type/parent changes. Track source changes and present reconciliation rather than overwriting.
- Limit to authorized Connection and asset scope; server/Gateway checks, not UI-only.
- Large workspaces: server-side paging if available; cancellation, bounded concurrency, batching, cache catalogs with explicit invalidation, partial-results warnings and retry policy.

## Logic association and data access

For each configured logic association resolve a **specific saved workspace connection and entity binding**, then select supported signal(s), value kind, unit and data interval/freshness. Do not pass PetroVisor SDK types into core domain, custom Python/C# logic code, or public API DTOs. Logic modules use neutral input parameters/data snapshots; provider-specific modules may specify provider capabilities/mapping declaratively, but **workers must not call PetroVisor directly** (per CONTRIBUTING). Gateway/adapter owns provider I/O; execution and outputs remain subject to configured mode, authorization, audit and correlation ID.

Example workflow:
1. Operator selects connection/workspace, browse mode and source entity; explicitly associates an AutoPilot Asset.
2. Configure a logic module with named input signal(s) and unit(s), lookback window, minimum observation count, max acceptable age, null/NaN handling and proposed output mapping.
3. On execution, server/Gateway validates connection and entitlement; PetroVisor adapter retrieves stored static or timestamped signal values (documented `DataRequest` + `Scope` + data service). Preserve provenance, timestamps, units, quality and missing-value status.
4. Execute bounded C#/Python logic in existing sandbox/worker pipeline against immutable snapshot. Shadow results are recorded only; Recommended produces governed proposed output; Autonomous still requires existing policy and safety checks.
5. Result output is *not necessarily a physical setpoint*. A write to PetroVisor is only a **warehouse-data write** unless a distinct, documented downstream control path is proven. No CygNet/ForeSite equipment-control implied through PetroVisor.
6. Later, allow explicit approved publishing to a designated PetroVisor result signal after verifying overwrite range, concurrency, scopes, idempotency, authorization and read-back. Disable mutating SDK calls in read-only connection mode.

### Recommended v1 capabilities

`ConnectionTest`, `DiscoverEntityTypes`, `DiscoverEntities`, `DiscoverHierarchies`, `PreviewEntityAssociations`, `ReadStaticNumeric`, `ReadTimeNumeric`; consider string data based on concrete first logic case. **Not v1**: data source/connection administration, Acquire, Delete, arbitrary P# execution, workflow execution, metadata mutation, general-purpose writes or physical commands.

## SDK and compatibility isolation

One `PetroVisor` adapter assembly/project in this repo for now; both NuGet packages pinned to `3.12.2609.1` as the *candidate test baseline*. Check existing central NuGet package management and runtime packaging before adding references. NuGet package versions, AutoPilot adapter version, and deployed PetroVisor server version are separate axes. Log detected/declared server build if retrievable; record tested combinations rather than assume that the SDK version implies backward compatibility. Introduce adapter version forks only when tested API breaking changes warrant them. Do not infer ForeSite/WAMI version-contract support from PetroVisor SDK.

Authentication spike: confirm supported noninteractive service-account/token/discovery options, endpoint TLS/certificate behavior, workspace permissions and how secrets are kept in the existing Connections secret store. Redact secrets and personal names in logs. Never commit credentials/customer data.

## POC plan and acceptance evidence

**Slice A — buildable integration spike:** Inspect actual NuGet public APIs/reflection signatures, exact constructors, service contracts and package transitive dependencies; create isolated adapter project with `dotnet restore/build` and contract tests that do not need production credentials. Confirm .NET 10 distribution and packaging.

**Slice B — read-only connection:** Authorized saved connection -> SDK initialization/health -> chosen workspace; graceful auth failures, timeouts, cancellation, no credential leakage.

**Slice C — workspace and inventory:** Real fixture or test workspace with at least 2 entity types, one type-only catalog, 2 differently organized named hierarchies, and orphan/unrelated entities. Compare `Workspace > Type > Entity` browse against named hierarchy browse. Verify no duplicate bound entities across views.

**Slice D — governed import:** Preview/review/apply source bindings; repeat import is idempotent; rename/deletion/move/type change produce warnings/review, not quiet destructive mutation. Confirm authorization and transaction boundaries.

**Slice E — data/logic:** Retrieve one static and one recent time series for bound entity, with tested units, missing data, NaN, timezone, freshness and range constraints. Run one existing Shadow logic without vendor access in worker.

**Slice F — optional writes only after decision:** Separate reviewed capability, dedicated test workspace and sacrificial output signal. Verify precise `Save` replacement scope, concurrency, authorization, approval, idempotency, read-back, audit; no real equipment control.

Evidence checklist: unit/contract tests, compatibility matrix with tested environment and SDK, integration test fixture/screenshots without customer information, endpoint/auth failure matrix, nested hierarchy/orphan examples, paging/large workspace behavior, CI + exact-head checks. **No live provider test has been performed for this document**.

## Open questions to resolve via testing

1. Is the canonical immutable workspace/entity identifier publicly accessible, or only names? What survives rename, alias change or workspace migration?
2. Can an endpoint list workspaces, and can a service account scope credentials to one workspace?
3. Does `EntityType` hierarchy exist? Treat entity types as *flat classification* until API evidence proves otherwise.
4. Are time-dependent hierarchy relations needed for current operating view or historical playback, and how should effective time be chosen?
5. Can a source entity appear in multiple named hierarchies, what happens on concurrent imports, and what is desired AutoPilot primary parent?
6. Are signal availability and signal data coverage queryable per entity without N+1 requests? How expensive is scanning large inventories?
7. What signals should be allowed for outputs, and how do we ensure PetroVisor writes do not trigger unexpected workflows/downstream control?
8. Does SDK support reliable async/cancellation for all necessary operations? Are proxy instances thread-safe?
9. Do we want workspace represented as an AutoPilot persisted Group or source-only root? Test existing schema before choosing.
10. How should data freshness, late arrivals, backfills, units and null/NaN be represented in recommendation evidence?

## Risk register and guardrails

- **Wrong relationships:** grouping by type is not an asset topology. Label synthetic nodes; preserve hierarchy selection and provenance.
- **Cross-workspace mixing:** composite source identities; workspace-bound credentials and stable connection.
- **Historical data mistaken as live:** freshness guard, timestamp provenance and quality rules before autonomous mode.
- **Data destruction via Save:** phase-1 read-only; isolated write evaluation.
- **SDK/server mismatch:** tested compatibility matrix, explicit degraded/unsupported state.
- **Credential exposure:** existing encrypted secret references, no repository secrets, no log echo.
- **Scale and drift:** pagination, revision detection, explicit review, idempotent import.
- **Overengineering:** reuse current provider adapter contracts, no public plugin SDK or generalized schema engine until real case.

## Repository alignment

- Existing planning: PR #205 (simple-first Connections design and first adapter), PR #206 (configured logic association, source-bound reads, shadow execution).
- CONTRIBUTING.md mandates provider-specific code behind integration contracts, no direct provider APIs inside Logic Modules/Workers, authorization and correlation continuity, no stored credentials/customer data.
- This branch is a *POC research feature branch* and does not change executable code, public contracts, database schema, installer or CI. Use subsequent implementation commits on this branch only after reviewing actual current architecture and tests; keep first runtime slices small and independently verifiable.
