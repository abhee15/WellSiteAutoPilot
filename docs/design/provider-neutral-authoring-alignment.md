# Provider-neutral, asset-first authoring — alignment contract

**Status:** implementation design baseline, 2026-11-17. No runtime or permission change is authorized by this document. Live ForeSite/native CygNet acceptance remains a separate user-owned gate.

## Product boundary

WellSite AutoPilot authors, reviews, publishes, assigns and executes **Logic Modules** against authorized **Assets**. A module is not inherently a well algorithm, and an Asset is not inherently a well. A well is one supported domain-specific Asset kind.

The module declares **what it needs** (roles, capabilities, typed input and output ports, parameters, validity policies), never **how to call ForeSite, CygNet or another vendor**. Adapters implement provider operations behind the existing Integration Gateway. Connections and per-Asset bindings choose concrete sources after or during authoring and are checked again when the module is assigned and run.

**Invariant:** Author a draft without an Asset. Choosing an Asset or Connection supplies *sample data and compatibility evidence*, not a permanent vendor dependency in the reusable module. A module may ultimately bind roles to different providers; do not create an alternative registry, generic execute-anything API or unchecked provider-specific scripting path.

## Existing architecture to extend, not replace

- #484: the versioned adapter capability catalog is the sole description of implemented operations, supported fields, typed result schemas and limits. Installed adapter support is **not** equivalent to Connection/Asset availability or hosted-module consumption.
- #302: manifest and runtime already support a bounded structured data path for arrays, records and card collections; do not regress to scalar-only assumptions or flatten structured outputs.
- #447: authoring may use authorized sample data but publishable requirements must remain portable and checked at assignment and runtime.
- Guided Authoring currently has two illustrative fixed rules: threshold uses `PumpFillage`; differential uses `CasingPressure` and `TubingPressure`. Its built-in sample cases are arrays of predetermined scalar/pressure readings. These are examples, **not** the platform authoring data model.
- `GuidedModuleContract` already inspects declared inputs/outputs and an opaque execution boundary. Internal calculations are only visible when a real supported declarative definition or recorded trace supplies them. Do not infer dependencies from input/output lists.
- The live preview currently searches imported Assets but labels the interaction as wells; its execution APIs are currently narrower than the proposed generic architecture. Renaming an affordance must not imply all Asset kinds and operations already work.

## Single schema across Workflow and IDE

Both authoring views must read and write the **same versioned draft representation**. Workflow renders structured forms and schema-driven guidance; IDE exposes code/declarations where the selected runtime truly supports authoring. Neither mode may own independent calculation semantics or quietly rewrite the other mode's unsupported content.

| Concern | Declarative module contract | Resolved later against authorized sources |
| --- | --- | --- |
| Module/version | identity, publisher, runtime, entrypoint, immutable revision | installed host compatibility |
| Asset roles | logical role IDs, cardinality, type/trait constraints | selected Asset IDs, Connections, source revisions and grants |
| Inputs | stable IDs, semantic quantity/capability requirement, type/shape, unit, access/current/history window, quality, age, required/optional | adapter operation, provider field mapping, concrete read availability and provenance |
| Outputs | stable IDs, semantic type/shape, unit, quality/provenance policy | execution result and persisted run evidence |
| Parameters | versioned schema, validation constraints, defaults | configuration values per assignment |
| Logic | supported declarative expression/graph **or** opaque signed module boundary | isolated execution and optional host-validated, module-reported calculation trace |

Be explicit about capability identifiers and shape versions. Do not let provider DTO names become portable module identifiers. Where today's contract cannot express a field or a role, evolve the versioned contract and validators before adding an editor control that pretends to persist it.

## Capability discovery and compatibility

1. **Catalog:** expose only what the adapter actually implements, grouped by capability family (Surveillance, Daily averages, Well trend, Well test, Card data, configuration, or future provider-defined families). Distinguish descriptor/schema support from live applicability.
2. **Authoring without an Asset:** let the author add/remove/reorder required inputs and outputs and define parameter constraints from supported capabilities/typed schemas. Unavailable generic editing should be marked inspect-only, not silently mapped to pump fillage.
3. **Optional sample Asset:** use authorized Connection plus Asset selection, then discover relevant operations/fields with provider-supplied types, units, quality, timestamps and availability. Dynamic fields can vary by provider, Asset and time window. Missing fields are missing; never invent placeholders or assume a fixed pair of Surveillance quantities.
4. **Compatibility check:** evaluate exact role/cardinality, required traits, available capability, field shape/unit, freshness/quality and permissions. Return separate statuses: supported/unsupported, missing, rejected, unavailable and not-checked. Show the reason; do not convert a sample pass into a guarantee for other Assets.
5. **Assignment and run:** recheck compatibility and authorization against the actual selected Assets, binding revisions and source Connections. No implicit cross-Connection identity or authority.

A future module can have more than one source role (e.g. read from ForeSite and request a WAMI calculation), but each independently authorized source role must be declared and resolved. Multi-provider orchestration is a distinct implementation milestone.

## Sample cases are first-class test fixtures

A Sample case is a **versioned, reproducible input fixture with an expected assertion**, not an arbitrary "Run" button or a live-reading display.

- **Manual/synthetic:** author supplies valid typed values/arrays/cards, boundary cases, null/missing/quality/freshness cases and expected recommendations, outputs or diagnostics.
- **Captured from authorized sample data:** snapshot a bounded, explicitly chosen read with Connection/Asset identity, source timestamp/quality, schema, units and provenance. Capture is a one-time fixture action; never embed credentials and never silently turn a live read into portable required input semantics.
- **Execution:** run only through the supported preview/isolated host path for the draft's actual runtime; mark a case unexecutable when no safe executor exists. Keep the accepted assertion distinct from the actual observed result. Retain draft revision, fixture version and execution evidence.
- **Validation:** reject wrong shapes, unknown inputs, nonfinite numeric values, missing required inputs and over-limit payloads. A test passing against one Asset does not prove all Assets are compatible.
- **Trace:** render intermediate results only when actual validated run evidence contains them; identify them as module-reported, and do not show unrelated run values in a draft.

Existing fixed threshold/pressure examples can remain **starter templates**, but must not be the only input/output or Sample case model.

## UX hierarchy and language

- `Authoring` page heading stays stable, with the selected module name shown **once** in the draft identity/editor area rather than repeated as an inconsistent page heading plus input. Navigation `Drafts / Reviews` uses shared `PageTabNav`; editing `Workflow / IDE` uses shared `SectionTabs`.
- Give input, output, parameter and test-fixture editors clear, task-specific placement. A module dropdown is navigation, not a second title.
- Use existing `PanelHeader`, `ActionButton`, form primitives, design tokens and responsive breakpoints. Fix IDE action alignment and the preview accordion using existing design conventions, with keyboard/accessibility coverage.
- Say **Find Assets**, **Select Asset**, **Preview with an Asset**, **Check Asset compatibility** at generic surfaces. Use **well** only for a domain-specific capability/requirement or API that is genuinely well-scoped, and show unsupported Asset types honestly.
- Keep sample/fixture editing separate from live preview. Show read-only inspectors for unsupported calculation types; no placeholder controls that seem executable.

## Dependency-ordered implementation slices

1. **P0 — terminology and layout:** shared header/tab/button patterns, remove redundant title, asset-neutral generic labels; preserve current well-scoped backend limitations explicitly. Frontend interaction, 1440/1024/768 browser and accessibility tests.
2. **P0 — draft contract model:** define the versioned, validated editable input/output/parameter/role representation, persistence and backward-compatible migration of threshold/pressure templates. Verify authoring without an Asset. Do not enable edit buttons before the API can save and validate them.
3. **P1 — dynamic capability to port builder:** query the existing adapter catalog for implemented descriptors and optional asset-specific discovery, then map supported semantic quantities and shapes into portable draft ports. Include variable counts (including no quantities) and mixed providers in fixtures; keep live acceptance distinct.
4. **P1 — fixture editor:** typed/manual/captured Sample cases, stable revision identity, assertions, bounded isolated preview execution and clear stale-evidence behavior. Preserve fixed examples as explicit starter fixtures.
5. **P1 — execution evidence:** bounded, authorized trace inspector in completed Runs; only optionally correlate exact module/version/revision/provenance to authoring. Never synthesize intermediates.
6. **P2 — multi-role/multi-provider binding:** typed role/Connection/Asset resolution with independent grants, Gateway request contracts, audit and fail-closed execution. Introduce only when single-source flow is complete and source semantics are established.

## Quality gates: no architectural drift

- No provider API name, vendor field ID, well-only identifier, connection credential or particular Asset ID is required in a reusable module definition.
- A draft with zero sampled Assets is valid; binding to an Asset verifies the declared role and port contract rather than mutating it silently.
- Verify a scalar threshold starter and a structured-card/module fixture without changing their production semantics; exercise variable input counts and both supported/unsupported capability paths.
- Demonstrate that another provider can satisfy the same semantic port through its own adapter without modifying the authoring UI. Where this is not yet true, retain an explicit unsupported result.
- No unsafe runtime code evaluation, provider writes, unverified trace values, false live support or widened data access. Tests cover permission loss, stale revisions and source revocation.
- Each PR names the design contract it advances, protects the existing `develop` checks, and distinguishes synthetic CI from local live ForeSite/CygNet acceptance.

**Decision:** architecture contract and runtime truth first; stylistic changes second; dynamic controls only after persistence/validation exists. Do not solve a single screen by constraining the whole platform to that screen's starter rule.
