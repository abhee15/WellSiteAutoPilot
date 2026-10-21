# PetroVisor adapter — POC research and implementation proposal

Status: research / design only; revised with owner scope clarifications; no runtime changes or live PetroVisor validation.
Date: 2026-10-20 (updated)
Branch: `feature/poc-petrovisor-adapter`
Target: discussion before adapter implementation.


## Decision summary — updated with owner guidance (2026-10-20)

**Keep it small and consistent with CygNet discovery.** PetroVisor is a workspace-scoped data-warehouse provider, not a SCADA/controller provider. Integrate solely with **existing data in a workspace**, never PetroVisor's ingestion connections or external data sources.

1. **Windows single sign-on is supported by PetroVisor** (owner-confirmed). Prefer Windows-integrated authentication; validate which identity the SDK uses from the Windows service/Gateway host and whether the deployment service account is granted access. Interactive user SSO vs unattended Gateway SSO is a deployment question, not an invitation to add a second auth framework.
2. **Workspace is the top-level source boundary**. Enumerate its Entity Types and Entities as a flat discovery inventory; group by Entity Type in the UI if helpful: `Workspace > Entity Type > Entities`. The type grouping is **not** an imported parent-child physical asset hierarchy.
3. **Do not import PetroVisor named hierarchies in v1.** Import/reconcile selectable entities using existing reviewed-source-binding workflow. Users create and maintain their own AutoPilot hierarchies after import, as with CygNet Facilities. Keep source bindings intact even if AutoPilot assets move between groups.
4. **Signal selection and business meaning belong to each Logic Module.** Adapter provides generic read/write operations for a module-supplied entity, signal, unit and optional timespan. No broad signal discovery, mapping UI, or required signal catalog in the connection adapter.
5. **No generic freshness policy** in the PetroVisor adapter or first AutoPilot binding workflow. Use stored data as requested by logic; propagate any available timestamps and missing-value indicators, without gating execution for age. A particular logic module may apply domain-specific policies if needed.
6. **Writes go back only to PetroVisor's stored data**, not to any SCADA controller or physical setpoint. Validate PetroVisor `Save` behavior carefully, especially the documented replacement of data within affected ranges. Govern writes using existing mode/approval/auth/audit behavior where applicable.
7. Retain a separate PetroVisor adapter project with pinned SDK packages and capability/version notes. Reuse existing Connections, Gateway, Asset and Logic frameworks; do not create plugin-hosting/contract infrastructure prematurely.

Status: documented design only; no PetroVisor live test or executable SDK spike yet.

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

A saved Connection identifies a PetroVisor server/environment **and one selected workspace**. In v1, use a separate workspace-bound connection for each workspace. Prevent unreviewed endpoint/workspace replacement if bound AutoPilot assets depend on it; honor existing protected deletion and permission rules.

Canonical discovery/source identity (final field choice subject to SDK/server tests):

```text
provider = petrovisor
connectionId = <AutoPilot saved connection ID>
workspaceKey = <vendor immutable workspace ID if exposed; otherwise scoped canonical name>
entityKey = <vendor immutable entity ID if exposed; otherwise workspace-scoped canonical name>
entityType = <PetroVisor classification>
```

Source binding must never depend solely on a display name, and AutoPilot asset reparenting must never change the underlying PetroVisor entity binding. Entity names are described as workspace-unique; immutable ID availability and rename behavior still need confirmation.

### Initial discovery model

```text
PetroVisor Connection: Server A / Production Workspace
└── Workspace: Production            (source/root namespace)
    ├── Entity Type: Field            (optional UI classification group)
    │   └── Field A                  (source Entity; importable)
    ├── Entity Type: Pad              (optional UI classification group)
    │   └── Pad 01                   (source Entity; importable)
    └── Entity Type: Well             (optional UI classification group)
        ├── ESP Well 101              (source Entity; importable)
        └── ESP Well 102              (source Entity; importable)
```

This is a *browse/classification tree only*. **Do not persist the source entity-type levels as physical parent-child relationships.** Import entities as existing asset inventory / provider-bound assets with the appropriate source type. Then allow operators to organize relationships on the AutoPilot side independently. Workspace must remain the logical source boundary; whether it requires a persisted Asset Group depends on existing asset-schema conventions, not a new compulsory asset type.

```text
PetroVisor entities: Field A, Pad 01, ESP Well 101, ESP Well 102
       | reviewed import; stable workspace/entity bindings
       v
AutoPilot asset inventory (initially no imported source relationships)
       | operator builds own hierarchy, as with CygNet facilities
       v
AutoPilot hierarchy: Region West > Pad 01 > ESP Well 101 / ESP Well 102
```

PetroVisor **named Hierarchy** and `IHierarchyRelationshipService` can remain optional future research; avoid calling, importing, validating, or reproducing them in the first POC. Do not assume Entity Type defines a relational hierarchy. If operators later request source hierarchy synchronization, treat it as a separate opt-in feature with explicit conflict policy.

### Minimal discovery/import contract

- Test authorized workspace connection; enumerate available Entity Types and Entities within selected workspace.
- Support paging/batching for large catalogs, entity type display and basic filters/search; don't scan signal catalogs.
- Preview the entities selected for import: new, already bound, missing/renamed and type mismatches when detectable; explicitly apply using existing reviewed asset-import flow.
- Bind each entity to selected workspace + connection + stable entity identifier where available. Repeat import must not duplicate the same source entity, or overwrite the AutoPilot-defined hierarchy.
- Keep imported entities even when AutoPilot users reorganize them; reimport is identity reconciliation, **not hierarchy synchronization**.
- AuthZ remains enforced in Gateway/server. Audit and correlation ID should follow existing repository conventions.


## Logic association, reads and writes

Logic modules declare what they require from PetroVisor: source-bound entity, signal name/ID, value type, optional unit, and for time-series data the requested range. Do **not** require the adapter to discover signals, own domain-specific signal mapping or decide if a stored measurement is too old. The logic author owns requested fields and any signal-specific validation. The adapter translates these generic requests to documented PetroVisor `DataRequest`, `Scope`, and typed data services.

The adapter should preserve returned timestamps, units and null/NaN values where applicable **as data**, not enforce a global freshness gate. Signal names/units may be validated upon an actual read with actionable errors (unknown signal, unauthorized, wrong type), without a prebuilt catalog.

Execution boundary: Logic Modules/Workers receive neutral data snapshots and do not access PetroVisor SDK, backend data stores or provider credentials. Current Gateway/adapter handles provider I/O and preserves authorization, execution correlation and audit; existing Shadow, Recommended and Autonomous modes govern recording, approval and permitted publication.

Example path:

1. Select workspace-bound PetroVisor connection, discover/import ESP Well 101 as an AutoPilot asset, and optionally place it in an operator-defined AutoPilot hierarchy.
2. Associate logic with that bound asset; logic declares `ESP Well 101 + Frequency + Hz` (example signal only, not assumed to exist), plus requested time range/value kind.
3. Adapter reads **already-stored** PetroVisor values. No attempt to connect to source systems or discover upstream SCADA ingestion.
4. Logic runs under existing controlled execution pipeline and returns a result.
5. In Shadow, record only in AutoPilot; when authorized under configured mode, publish a result **to PetroVisor database** at a specific target entity/signal. PetroVisor is not a SCADA controller path; do not build hardware command, CygNet or ForeSite control dispatch into this adapter.
6. Before enabling writing, use a dedicated test workspace and output signal to prove exactly whether `Save` overwrites existing points/ranges, semantics of empty/NaN, concurrency behavior, retries and read-back. Only then expose the write capability to approved execution modes.

### Recommended POC capabilities

**Discovery:** `ConnectionTest`, `DiscoverEntityTypes`, `DiscoverEntities`, `PreviewEntityAssociations` / reuse existing import.

**Logic I/O:** `ReadStaticNumeric`, `ReadTimeNumeric`, `WritePetroVisorResult` after isolated write tests; extend typed variants when a real module needs them. No required `DiscoverSignals` / `DiscoverHierarchies`.

**Not in scope:** PetroVisor data ingestion/Acquisition/Data Sources/Connections, copying PetroVisor native hierarchies, SCADA/controller actions, arbitrary P# execution, generic signal configuration screen, or global data-freshness monitoring.

## SDK and compatibility isolation

One `PetroVisor` adapter assembly/project in this repo for now; both NuGet packages pinned to `3.12.2609.1` as the *candidate test baseline*. Check existing central NuGet package management and runtime packaging before adding references. NuGet package versions, AutoPilot adapter version, and deployed PetroVisor server version are separate axes. Log detected/declared server build if retrievable; record tested combinations rather than assume that the SDK version implies backward compatibility. Introduce adapter version forks only when tested API breaking changes warrant them. Do not infer ForeSite/WAMI version-contract support from PetroVisor SDK.


Authentication spike: PetroVisor supports **Windows SSO** (owner-provided requirement). Determine the specific supported SDK/client mechanism (e.g., HTTP Negotiate/default Windows credentials if applicable; do not assume until inspected); verify availability in the running Gateway/Windows service context, Windows principal under which outbound calls execute, permission to the workspace and whether impersonation/delegation is necessary. Prefer the existing unattended service identity as the initial supported model, rather than expecting the browser user to flow through a server hop. Test 200/401/403 cases and error messages without leaking identity tokens. Keep endpoint TLS validation and compatibility testing. Do not require username/password as the default supported method, although public sample code demonstrates that constructor style.


## POC plan and acceptance evidence

**Slice A — SDK + Windows SSO spike:** Inspect NuGet concrete interfaces, constructors, supported authentication and package dependencies. Create adapter project (following repository project conventions) and compile against pinned SDK. Prove outbound Windows-integrated authentication from a realistic Gateway Windows process/service identity; document alternate unsupported scenarios.

**Slice B — workspace and entity discovery:** Select workspace, enumerate entity types and all entities, show the `Workspace > Entity Type > Entities` browse classification. Confirm missing types, large/paged workspace behavior, duplicate display names and failure modes. **No PetroVisor hierarchy imports.**

**Slice C — association/import:** Reuse current preview/apply Connections asset import with source bindings. Reimport is idempotent; users can reorganize entities into independently maintained AutoPilot hierarchies without source-binding changes. Verify mismatches/renames, workspace constraints and authorization.

**Slice D — logic read:** One configured Shadow logic obtains a module-defined numeric stored signal through the adapter for a bound entity, using specified unit/value type/range. No catalog discovery and **no global freshness restriction**. Test missing data/NaN/type mismatch and concurrent executions.

**Slice E — PetroVisor result write:** Safely exercise `Save` only in an isolated workspace/test signal; document exact range replacement and retry behavior, then integrate governed modes, audit/correlation and read-back. Physical-control verification is **not applicable**, because only PetroVisor database data is written.

Evidence checklist: unit/contract tests, server/SDK version matrix, Windows service-account SSO behavior, workspace/entity inventory with safe synthetic test data, source-binding idempotency, operator-created hierarchy independent from source, Shadow read end-to-end, safe write/read-back and exact-head CI validation. **As of this documentation update none of these tests has been executed.**


## Open questions to resolve via testing

1. Which PetroVisor SDK path enables Windows-integrated SSO? Does service/Gateway process identity work without delegation? What Windows and hosting constraints apply?
2. How is one workspace selected in the API; how can permissions restrict the unattended identity to authorized workspaces?
3. Is there a durable immutable entity/workspace identifier? How do rename, missing entities and workspace migration affect source bindings?
4. Does a test workspace with mixed Entity Types map cleanly into existing AutoPilot Asset Types and import UI, without enforcing artificial physical parents?
5. Can user-defined AutoPilot hierarchy edits remain intact after repeated discovery/import and source entity changes?
6. Can logic supply signal name/type/unit/range entirely at execution time without a signal browsing step, and what useful errors are returned on mismatch?
7. For output, what exactly does PetroVisor `Save` replace? Can append/upsert single data points safely be expressed? Which output entity/signal permissions are required?
8. Are SDK methods async/cancellable and clients concurrency-safe enough for Gateway execution?
9. Does the current asset schema need workspace persisted as a group, or can it remain a displayed connection-scoped root? Reuse rather than force schema changes.
10. Which PetroVisor server versions work with the pinned `3.12.2609.1` SDK baseline?


## Risk register and guardrails

- **Type grouping mistaken for asset topology:** Present Entity Type as browse classification only; AutoPilot owns asset hierarchies.
- **Windows SSO runtime mismatch:** Prove identity at the Windows service/Gateway host, not solely in a browser or interactive developer shell.
- **Cross-workspace mixing:** Key binding by connection + workspace + canonical entity identity; guard connection endpoint/workspace changes.
- **Persisted data replacement:** PetroVisor `Save` may replace existing points in range. Guard output scope, retries and write permissions; test before enabling.
- **Provider SDK drift:** Pin dependencies, track SDK/server compatibility, make unsupported releases explicit.
- **Scope creep:** No source hierarchies, data ingestion, signal discovery or freshness service. Add only for proven module/customer requirements.
- **Control path confusion:** Persisted PetroVisor output **never** implies direct SCADA/controller actuation.

## Repository alignment

- Existing planning: PR #205 (simple-first Connections design and first adapter), PR #206 (configured logic association, source-bound reads, shadow execution).
- CONTRIBUTING.md mandates provider-specific code behind integration contracts, no direct provider APIs inside Logic Modules/Workers, authorization and correlation continuity, no stored credentials/customer data.
- This branch is a *POC research feature branch* and does not change executable code, public contracts, database schema, installer or CI. Use subsequent implementation commits on this branch only after reviewing actual current architecture and tests; keep first runtime slices small and independently verifiable.
