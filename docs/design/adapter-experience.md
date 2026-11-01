# Adapter experience: connect, understand, author

Status: proposal and probe. Branch `probe/poc-adapter-experience`, based on `feature/poc-integration`.

The product owner's goal: a user should be able to (1) connect to a software system through an adapter, (2) understand what that adapter exposes and (3) author logic modules against it.

## 1. What adapters expose today

| Where | What it says | Gap |
|---|---|---|
| Gateway `AdapterCatalog` (simulator, foresite, petrovisor, cygnet) | `AdapterDescriptor`: key, display name, description, settings version, settings fields (`Text/Url/WholeNumber/YesNo`, required, max length, help text), capabilities (`ProviderCapability(Kind, Name, ContractVersion)`), discovery grouping fields | No quantities, units, freshness, limits, cadence, read/write or examples. WAMI has a `WamiAdapter.Descriptor` but is **not in the catalog**. |
| Gateway `POST /adapters/{key}/validate`, `/test`, `/discover` | Validate settings, test (Healthy / NeedsAttention / Unreachable, with acting-as), discover (complete flag, warnings, unavailable fields, source scope) | Good basis for "test connection" and "browse". There is no "preview a value" call that users can reach. |
| Server `GET /api/v1/connections/adapters` | `AdapterResponse`. Capabilities are **names only**. | Kind and contract version are dropped. |
| `AdapterReadRules` (Integration.Contracts) | foresite → `foresite.well` {PumpFillage, AveragePumpFillage}. cygnet → `cygnet.facility` {PumpFillage, CasingPressure, TubingPressure, MotorAmps, RodLoad}. SurfaceCards goes through a separate `structured.current` capability. | Units, timestamp meaning and schema ids live in adapter code (the CygNet point map, ForeSite `ControllerScan`/`CardCapture`), so authors cannot see them. |
| Read budgets | ForeSite 6 s, CygNet 10 s, set inside the gateway providers | Not visible to anyone. |
| Cadence | Only the connection monitor's 60 s minimum | No per-adapter fastest schedule. The UX doc imagines "Fastest allowed schedule: 15 s (set by CygNet)". |
| Manifests | `requiredAdapterKey`, plus `LogicDataRequirement` (quantity, canonical unit, max age, value schema, required capability) | Authors must already know which quantity names and units each adapter serves. |
| `AuthoringStarters` (threshold-alert, card-calibration, observe-input) | Typed inputs (quantity and unit) | Not linked to adapters, and no starter sets `requiredAdapterKey`. |
| CygNet point map | `pointMap` setting `Quantity=UDC|unit;...`. MotorAmps is unmapped by default. | Mapping state is per connection and is not shown next to the quantity. |

Conclusion: the facts a module author needs (quantity names, units, types, timestamps, limits, health) exist in four places in code, and none of them is surfaced together.

## 2. Prior art (sources)

- **Airbyte protocol**: `spec`, `check`, `discover`, `read`, and a versioned protocol. Connector acceptance tests are run against every connector. https://docs.airbyte.com/understanding-airbyte/airbyte-protocol, https://docs.airbyte.com/connector-development/testing-connectors/connector-acceptance-tests-reference
- **OPC UA `MinimumSamplingInterval`**: the server declares "how fast it can reasonably sample" (0 = continuous, -1 = indeterminate). This is the model for a declared adapter cadence. https://reference.opcfoundation.org/Core/Part3/v105/docs/5.6.2
- **Power Platform custom connectors**:
  - summaries in sentence case
  - `x-ms-visibility` (important/advanced/internal)
  - `x-ms-dynamic-values/-list/-schema/-properties` for pickers and schemas driven by the backend
  - `x-ms-capabilities`
  - https://learn.microsoft.com/connectors/custom-connectors/openapi-extensions
- **Grafana backend plugins**: query data, resources, health check, metrics and streaming. Every data source ships a query editor. https://grafana.com/developers/plugin-tools/key-concepts/backend-plugins/
- **Home Assistant**:
  - config flow validates before an entry exists, and every field has a `data_description`
  - a scaffold script
  - an integration quality scale (Bronze to Platinum), with the tier recorded in the manifest
  - https://developers.home-assistant.io/docs/config_entries_config_flow_handler, https://developers.home-assistant.io/docs/core/integration-quality-scale/
- **n8n**: declarative and programmatic node styles, a starter repository, node versioning. https://docs.n8n.io/integrations/creating-nodes/build/reference/node-base-files/
- **Backstage**: `apiVersion/kind/metadata/spec` descriptors with links and annotations, and Software Templates (choose, fill inputs, review). https://backstage.io/docs/features/software-catalog/descriptor-format, https://backstage.io/docs/features/software-templates/
- **Terraform `providers schema -json`**: `format_version`. Minor versions are backward-compatible and consumers ignore unknown properties; consumers reject an unknown major version. https://developer.hashicorp.com/terraform/cli/commands/providers/schema
- **Kubernetes CRD versioning**: served and storage versions, and conversion. https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definition-versioning/
- **Zapier**: the "test authentication" call shows the response, bundle and HTTP details, plus a connection label. https://docs.zapier.com/platform/build/auth
- **Node-RED help style guide**: summary line, typed Inputs/Outputs, then Details. https://nodered.org/docs/creating-nodes/help-style-guide
- **Ignition Tag Browser**: a tree with the current value, data type and provider for each tag. https://docs.inductiveautomation.com/docs/8.1/platform/tags/tag-browser
- **VS Code JSON Schema**: IntelliSense and validation driven by the schema. https://code.visualstudio.com/docs/languages/json
- **AsyncAPI**: channels and messages with payload schemas. https://www.asyncapi.com/docs/reference/specification/latest
- **AVEVA PI AF and PI Integrator**: elements and attributes with units of measure and templates. The pages could not be fetched, so this is from general knowledge and unverified. No source was found for "Kepler".

Common lessons:
1. Adapters describe themselves in machine-readable form, and every UI is generated from that description.
2. Keep spec, check, discover, preview and read as separate operations.
3. Make the test and preview responses visible to the user.
4. Use one versioned format with additive minor changes.
5. Run a conformance suite and a quality tier for each adapter.
6. Provide a scaffold.
7. Drive authoring pickers and autocomplete from the schema.

## 3. Recommended adapter self-description

This extends `AdapterDescriptor`. It stays in Integration.Contracts so that the adapter owns it.

```jsonc
{
  "formatVersion": "1.0",               // Terraform-style: minor = additive; consumers ignore unknown, reject unknown major
  "key": "cygnet", "displayName": "CygNet SCADA", "adapterVersion": "1.4.0",
  "settingsVersion": 2,
  "settings": [{ "key": "host", "label": "Host", "kind": "Url", "required": true, "helpText": "…", "visibility": "important" }],
  "auth": { "modes": ["windowsService"], "actingAs": "service account", "secretsHeldBy": "gateway" },
  "capabilities": [{ "kind": "CurrentData", "name": "current", "contractVersion": "1" }],
  "discovery": { "objectKinds": ["cygnet.facility"], "groupingFields": ["site", "type"], "maxObjects": 50000, "maxImport": 5000 },
  "read": {
    "resourceKind": "cygnet.facility",
    "quantities": [{
      "name": "CasingPressure", "label": "Casing pressure", "valueKind": "Number", "unit": "psi",
      "timestamp": "SourceSample", "typicalFreshnessSeconds": 60,
      "needsMapping": false, "schemaId": null, "requiredCapability": null
    }]
  },
  "write": { "supported": false, "commands": [] },  // when present: effect, confirmation, reversibility
  "limits": { "readTimeoutSeconds": 10, "maxValueBytes": 65536, "maxReplyBytes": 262144, "maxConcurrentReads": 4 },
  "cadence": { "minimumSeconds": 15 },              // OPC UA MinimumSamplingInterval analogue; null = undeclared
  "health": { "test": true, "checkIntervalSeconds": 60, "outcomes": ["Healthy", "NeedsAttention", "Unreachable"] },
  "examples": [{ "starterId": "threshold-alert", "quantities": ["PumpFillage"] }],
  "quality": "bronze"
}
```

Rules:
- `quantities` are the single source of truth for `AdapterReadRules`, the gateway units and the authoring pickers.
- Manifest validation checks each `LogicDataRequirement` (quantity, unit, value schema, required capability, max age vs freshness) and the schedule vs `cadence.minimumSeconds` against the descriptor.
- Per-connection state (CygNet point-map coverage, last test) is a separate *connection profile*, not part of the adapter descriptor.

## 4. User journeys

**Connect**
1. Go to Connections, then New connection, then choose an adapter. The card shows its summary, what it reads, and whether it is read only.
2. Fill in the settings fields, which are generated from the descriptor. The help text is shown; advanced fields are collapsed.
3. Choose **Test**. The result shows the outcome, acting-as, latency and (for engineers) the raw detail, Zapier-style.
4. Choose **Discover**, then browse and filter by grouping fields and import (exists today).
5. Map quantities where the adapter needs it (CygNet point map). Show coverage per quantity.

**Understand**
1. Open Connections → **Adapters** tab, then pick an adapter.
2. See its description, read/write status, quantities (type, unit, timestamp meaning, needs mapping), capabilities with contract versions, settings, grouping facts, limits, fastest schedule and the health of the connections that use it.
3. Next: preview a live value for one quantity on one imported object (like Ignition's current value), and see point-map coverage for each connection.

**Author**
1. From the adapter page, choose "Start from an example" to open authoring with the starter, with `requiredAdapterKey` pre-filled.
2. The inputs picker lists the descriptor's quantities, filtered by unit and type compatibility. Structured inputs show their schema id.
3. The manifest editor gets JSON Schema autocomplete. The quantity, unit and capability enums come from the descriptor.
4. Preview runs the module against live values on a chosen asset.
5. Validation errors explain the problem in plain words, for example: "CygNet does not serve MotorAmps on this connection until it is mapped" or "Schedule 5 s is faster than CygNet's 15 s".

## 5. Adapters tab contents

The prototype shows these:
- list of adapters
- description
- read-only or writes
- quantities table
- resource kind
- capabilities and contract versions
- settings and settings version
- grouping fields
- fastest schedule ("Not declared by this adapter yet")
- limits
- connection health counts
- matching starters
- manifest hint `requiredAdapterKey`

Later:
- live preview
- point-map coverage
- adapter version and quality tier
- changelog and compatibility notes
- links to docs

## 6. Adapter authoring SDK (for new adapters)

- **Contract**: `IAdapter` with `Describe()` (the descriptor above), `Validate`, `Test`, `Discover`, `ReadCurrent` and optionally `Write`. All return typed results; nothing throws for domain outcomes.
- **Conformance suite** (Airbyte-style acceptance tests): it runs against any adapter and a recorded or simulated source. It checks:
  - the descriptor parses
  - every quantity is returned with its declared unit and timestamp kind
  - the size and time limits are honoured
  - test and discover are deterministic
  - unknown settings are rejected
  - settings-version migration works
- **Scaffold**: `dotnet new wellsite-adapter` produces the project, descriptor, simulator fixture, conformance test project and README (like the Home Assistant scaffold and the n8n starter).
- **Quality scale** (bronze/silver/gold), recorded in the descriptor and shown on the tab.

## 7. Phased plan

| Phase | Priority | Work |
|---|---|---|
| 0 (probe, this branch) | done | Read-only `GET /api/v1/connections/adapter-profiles` built from catalog, read rules, `AdapterQuantities`, connections and starters. Adapters tab. |
| 1 | P1 | Move quantities, units, timestamp, limits and cadence into `AdapterDescriptor` (gateway owns it). Return capability kind and version from `/adapters`. Register WAMI. Declare a minimum cadence per adapter. |
| 2 | P1 | Validate manifests against the descriptor (quantity, unit, schema, capability, cadence, freshness). Typed inputs picker in authoring. Starters with `requiredAdapterKey`. |
| 3 | P2 | Live value preview endpoint (gateway read for one object and quantity, permission-guarded). Point-map coverage per connection. |
| 4 | P2 | JSON Schema for manifests generated per adapter, for autocomplete. |
| 5 | P3 | Adapter SDK: scaffold, conformance suite, quality tier, descriptor `formatVersion` compatibility rules. |

## 8. Open questions for the owner

1. Should the minimum cadence be a hard limit (reject) or a warning, and who sets it: the adapter author or the site (per connection)?
2. Can site engineers add quantity names (for example new CygNet UDCs), or is the quantity vocabulary fixed per release?
3. Are point-map edits versioned and audited like settings, and are per-facility overrides needed?
4. Who may preview live values: connections.read, or a new permission? Previews hit production SCADA.
5. Should WAMI (calculation) appear as an adapter in the same tab, or as a separate "models/calculations" catalog?
6. Will write or command adapters ever be in scope, and if so, what confirmation and approval model applies?
7. Is a public adapter SDK (third-party adapters) a goal, or are adapters internal only?

## 9. What is prototyped

Server:
- `AdapterQuantities` (Integration.Contracts) declares, for each adapter, its quantities, units, value kind, timestamp meaning, schema and read budget. The gateway now uses those budgets.
- `AdapterProfiles.Build` (Application) joins descriptors, read rules, connection health and starters.
- The endpoint is `GET /api/v1/connections/adapter-profiles`, guarded by `connections.read`. It returns 503 when the gateway is down. `minimumCadenceSeconds` is always `null` until adapters declare one.
- Checks are in `tests/WellSiteAutoPilot.ConnectionImportTests/AdapterProfileChecks.cs`.

Web:
- A Connections | Adapters tablist on the Connections page. `#connections/adapters[:key]` opens the tab.
- A read-only `AdaptersPanel` that loads only when the tab is opened.
- Sentence-case strings in `localization.ts`, and `loadAdapterProfiles` with a response validator.
- "Open authoring" is shown only with `logic.manage`.

Not yet verified (see the commit message):
- the C# checks run
- the web typecheck, which needs `npm run generate-api`
- interaction tests (none were written for the tab yet)
