# Module Authoring workbench: data-flow clarity and provider abstraction
Status: proposed implementation design · 2026-11-16 · target: `develop`

## Decision and the central user question

**An author must be able to answer:** “Where did this value come from, how was it checked and transformed, what calculation or rule used it, and why did the module produce this output?” They should not have to understand ForeSite/CygNet endpoints, Gateway calls or point IDs to author portable logic. An advanced inspector can expose the exact path for debugging and audit.

Retain the dedicated **Configure → Module authoring** route for existing links, plus the existing **Logic → New module** entry. Preserve the agreed *Workflow* and *IDE/Expressions* perspectives over one canonical draft, the same selected item, scenario and evidence. Do not introduce a second authoring model or a separate canvas-only editor.

## Evidence reviewed (not inferred from a running browser)

- The archived Workflow and Expressions mockups: module outline, central selected editor, persistent simulator trace, compact status/header. They communicate the hierarchy much better than the current starter form, but the original preview had competing scroll regions.
- `src/web/src/views/GuidedAuthoringPage.tsx` on `develop`: two starter actions and a list of drafts are rendered as a long toolbar of buttons; both views share the same small name/threshold form. IDE adds a three-choice outline, while Workflow stacks sections. The scenarios inspector is present but does not show a linked rule-by-rule provenance trace.
- `GuidedLivePreview.tsx`: authorized asset lookup, per-input compatibility, current/history/production-day/surface-card inspection are inside a collapsible Live preview. `GuidedQuantityDiscovery.tsx`: capability selection and discovered quantities are nested inside another disclosure. The individual operations exist, but the author must mentally assemble the data path.
- `guidedAuthoring.css`: desktop 2-column Workflow vs 3-column IDE; outline moves to a horizontal row below 75em and panels stack below 56em. Responsive layout and focus need browser checks; CSS inspection alone cannot certify visual quality.
- PRs #613–#617 merge compatibility, discovery, and a shared editor; #618 (fillage input), #619 (trend sample inspection), and #620 (pane spacing) are separate passing-but-open changes at time of review. This document specifies the follow-up design; it does **not** claim the unmerged PRs are present in `develop`.

## Complex-model design drivers (these define the UX, not the guided threshold starter)

The current guided editor is a narrow executable slice. Treat the following real code/POC references as *design acceptance fixtures*, not claims that the web author can currently build them:

| Design fixture | Proven code/POC behavior | Required authoring representation |
| --- | --- | --- |
| [Structured card calibration](../../modules/samples/structured-cards/CardCalibrationModule.cs), [V2 manifest](../../modules/samples/structured-cards/manifest.json) | Paired load/position arrays and calibration record; reject duplicate IDs, unequal lengths, future dates; map scale/offset over each card, aggregate peak, emit calibrated collection and scalar peak | Typed collections/records, array alignment and per-record validation; named parameter and intermediate map/reduce steps; two output shapes; source → output lineage and rejection trace |
| [ForeSite card summary POC](https://github.com/abhee15/WellSiteAutoPilot/blob/poc/cards-e2e/modules/poc/foresite-card-summary/EntryPoint.cs) | Select CURRENT card from collection, reject malformed pairs, calculate extrema, stroke travel and newest-card age | Filter/selection conditions, multi-output fan-out, distinct card capture time from observation retrieval time; read-only preview |
| [ForeSite → WAMI composition POC](https://github.com/abhee15/WellSiteAutoPilot/blob/feature/poc-wami-provider/docs/design/wami-provider-and-multi-provider-logic.md) | Named data/model/calculation roles, independent connection authorization; pin model SHA and invoke governed WAMI analysis; distinct timeout/busy/license/model failures | A 1..N role/dependency map, artifacts separate from readings, calculation services with input/output types, independent permissions and provenance; explicit not-yet-runtime-supported state |
| [Complex authoring probes (#321)](https://github.com/abhee15/WellSiteAutoPilot/pull/321) | Editable scalar dependent calculations, candidate browser/console document, versioned decimal semantics, stale evidence and synthetic composition | Stable step/port IDs, expression authority, dependency validation, invalidation and round trip; the synthetic coordinator and browser probe are **not** production runtime capability |

The structured calibration is deliberately not a physical downhole-card solver. Use it to validate UI mechanisms, not as the ceiling of engineering complexity. A subsequent acceptance fixture must include *realistic nonlinear/iterative algorithm code* exposed as a code-backed step with declared ports, because general numerical algorithms cannot be reliably recreated from a manifest.

**Do not claim that the manifest reveals arbitrary calculations.** For declarative steps, edges come from versioned explicit references. For code-backed steps, show only declared inputs/outputs, step status and instrumented evidence. Internal C# / Python loops remain opaque unless the algorithm explicitly exposes trace events. A graph drawn from guesses would be worse than no graph.

## Information architecture: one workbench, progressive disclosure

```text
Module title · Draft status/version                           Save   Validate   Submit
Workflow | Expressions                              Test context: None / Select sample well
─────────────────────────────────────────────────────────────────────────────────────────
Outline / Add                 Selected step (primary scroll)          Scenario & trace
Inputs                        ┌────────────────────────────────┐       Test case / Run
Parameters                    │ Selected calculation / step    │       Expected vs actual
Calculations                  │ Value flow                      │       Deciding rule
Rules                         │ Inputs → transform → result     │       Input evidence
Outputs                       │ Types · units · provenance      │       Missing/stale reason
                              │ [Configure fields / references]│
                              └────────────────────────────────┘
─────────────────────────────────────────────────────────────────────────────────────────
Reviewable: source requirements · validation findings · output contract · no field writes
```

- **Header**: identity, saved revision, unsaved/stale-evidence state, high-value actions. Put `Workflow | Expressions` in a compact view switch below the title; retain selected item, scroll context, test case, and evidence across changes. “IDE” is only an accurate label once the rich expression and rule editor exists. For now the UI may say “Expressions” while preserving the view state ID.
- **Outline**: grouped, named logical elements (Inputs, Parameters, Calculations, Rules, Outputs), not provider APIs or hardcoded starters. Include a compact **Add** menu and dependency hints. The blank state has a single clear **Add input** action.
- **Center**: edit the selected step and show its *actual* upstream and downstream relationships. For a collection, code-backed function or multi-provider model, start with a selectively expanded dependency graph **derived from the draft**, not independently editable edges. Clicking an upstream quantity navigates to its input definition; clicking a deciding rule navigates to that row. Avoid a full freeform n8n canvas as the mandatory experience.
- **Inspector**: persistent, resizable or collapsible Scenario / Trace on desktop. Run deterministic canned tests independently of any live connection. Show actual vs expected, matched rule, input values (units, age, quality and acquisition time), expressions and output. A live read is marked **Read-only sample**, not submission evidence, and never implies a write.
- **Single principal scroll**: no nested page/editor/inspector scroll competition on laptop. Tablet: outline in a drawer, inspector in a sheet; phone: submission/review only. Keyboard/touch reference insertion is required; drag-and-drop, if explored, is optional enhancement.

## Core abstraction the user sees

Keep three distinct nouns and states; do not conflate them:

1. **Module input (portable contract):** e.g. `PumpFillage (%)`, role `producingWell`, acquisition `current`, required quality/freshness; authored before selecting an asset. May describe `daily average`, `well test`, or bounded `history`, but publishability depends on supported runtime semantics.
2. **Preview source (optional test context):** authorized Connection + imported Well + one available adapter capability. Show `ForeSite · Well A · Surveillance` *only* when selected. Inspect provider-supported/observed fields and metadata through Server/Gateway. Do not bake the sampled asset ID or provider point ID into the portable draft.
3. **Deployment binding (after approval):** map module roles/inputs to authorized asset(s), exact provider connection and any required point/command target. Check compatibility on selection and again before execution; preview compatibility is not execution authorization.

A normal input card should read like:

```text
Pump fillage   • %   • Current reading   • Required
Source requirement: Producing well → Pump fillage
Sample context: ForeSite / Well 1403              [Change well]
Resolution: Surveillance via ForeSite adapter     [Inspect details]
Observed: 52 % · Good · 4 min old                 [View history]
Connected to: Low fillage rule → Recommendation   [Show path]
```

The sample and observed value are **illustrative**, not claimed live data. When no sample well is selected: show “Portable input defined · Select an authorized well to check compatibility.” If the asset lacks a measurement, show *Unsupported*, *Missing*, *Rejected*, or *Provider unavailable* rather than a blanket red “incompatible.”

**Adapter abstraction**: Present provider-neutral operations first: “Current reading,” “Daily average,” “Trend history,” “Latest reported well test,” etc. The expanded inspector may reveal `Connection → Adapter capability → provider API/point → normalized reading`, including conversion, unit/sourceUnitKey, original observation time, freshness, quality and exact outcome. Never pretend an observed well-test field is a validated authorable numeric binding; current discovery lists metadata/field names, not guaranteed usable values. No numeric ForeSite applicability IDs are fabricated.

**Multiple providers**: Model logically named provider roles 1..N in contract and dependency projection (e.g. production data and controller data); do not limit the UX to two fixed columns. The initial guided/runtime slice still supports only the proven paths. Mark unavailable combinations explicitly, rather than implying a cross-provider binding can currently execute.

## Observable flow and failure-state semantics

The trace's minimal chain is:
`Module requirement → [optional preview binding] → adapter capability → normalized typed reading → data policy → calculation/condition → output proposal`.

Each node/edge provides a selected-item explanation and runtime/test evidence state: `Defined`, `Observed`, `Satisfied`, `Missing`, `Unsupported`, `Stale`, `Bad quality`, `Rejected`, `Provider unavailable`, `Not checked`, or `Not supported for publication`. Graph lines represent references and dependencies, **not actual field control commands**. A detected provider failure, ambiguity, stale reading or bad quality must not silently fall back to a different basis; fallback is permitted only when explicitly defined for *inapplicability* and backed by the same policy. Preserve original well-test timestamp and status.

For each output show **Recommendation / Value / Proposal** with units, decision rule and guardrails. Clearly separate Dry run/Simulation, authorized live read and published Shadow run. Shadow produces zero control commands. A future Write target is a separate controlled configuration and authorization step.

## Interaction exemplar: structured card calibration, then multi-provider analysis

1. Open a saved complex authoring project (or choose a complex example from the **Examples** library). The header identifies the current module and draft status. No permanent row of template buttons; templates/examples belong to a creation entry point, not the editor.
2. The outline groups **Data & roles**, **Parameters**, **Operations**, **Conditions**, **Outputs** and **Tests**. Stable IDs and typed ports—not display names—bind references. On a large project each group can collapse, search or filter.
3. Select **Calibrate loads**. Its centered step editor shows `cards: SurfaceCard[]`, `calibration: {loadScale, loadOffset}`, the per-load expression, validation, and the two consumers. A compact dependency path identifies the upstream card collection and downstream per-card peaks and final aggregate. Show unit/timestamp/schema warnings at the affected edge.
4. The side inspector displays a fixed synthetic test case. Selecting an output highlights only its upstream path; selecting a rejected card shows exactly which ID, array-length, timestamp or quality constraint failed. The user can expand evidence and inspect an original card curve without losing the calculation context.
5. Change the `loadScale` parameter; automatically mark affected downstream results and submission evidence stale. Re-run through the authoritative evaluation/test path, not independent browser arithmetic. In the early slice, unsupported execution reports **Cannot run this step in browser** rather than fabricating success.
6. On a separate complex project, show three independent provider roles: **Well readings (ForeSite)**, **Engineering model (model source)**, **Engineering calculation (WAMI)**. The diagram displays which role supplies what, model pin/version and calculation result, and per-role authorization/availability. A failed WAMI license or stale model must stop the affected path with a precise diagnostic; don't show a plausible estimated value.
7. Let authors switch **Dependencies / Workflow** and **Code / Expressions** without duplicating the graph or losing selections. Source code functions remain opaque nodes with declared typed ports. Continue saving/reviewing validated immutable revisions; runtime publishability remains gated on implemented contracts, not the design's expressiveness.

## Samples, navigation and usable screen area

- **No “Guided modules” banner** inside the project editor. The page destination is **Module authoring**; once open, the main title is the module name, status, revision and Save/Validate/Submit actions. Reuse the product's `PanelHeader`, `PageTabNav`, `SectionTabs`, `ActionButton`, `Notice`, form fields and design tokens rather than a new local design language.
- **Separate browse/create from edit** using the existing master-detail or route pattern: project list (search, recent, owner, status), `New module` action, and a **Create** dialog/page with `Blank`, `Examples`, `Import` (only when supported). Examples are a discoverable catalog entry (e.g., structured-card calibration, composed engineering calculation), not a permanent button strip. The sample *data fixture* is different: it belongs in **Tests / Preview**, not alongside project templates.
- **Visual workbench priority**: selected data/calculation/condition step uses most of the width, with a collapsible outline and inspector. Header and mode switch remain compact. Use a focused dependency view with stable typed links, not a mandatory sprawling n8n canvas or infinite card wall. Details open contextually when selected.
- **Progressive complexity**: an author creating a simple rule only sees relevant properties; advanced model authors can inspect schema, collections, role binding, code, errors and test trace. Avoid always-on panels that contain controls with no relevance to the active step. On smaller laptop/iPad widths, one secondary pane at a time.
- **Visual-design review** must measure the existing application header/nav, actual available viewport height, page title duplication, panel padding, focus path, scroll behavior, tab order and accessible keyboard/touch interaction at representative widths. Source-level CSS alone cannot certify good spacing.

## Iteration and engineering boundaries

**Slice A — unlock existing green branches and baseline**: review then merge #618 → #619 → #620 in dependency order; verify CI/visual screenshots at each exact merged head, avoid false success claims. Capture 1440/1024/820/768 widths and fixed scenario fixtures. Create baseline screenshots of both perspectives and review screen.

**Slice B — source-to-consumer projection**: derive a canonical dependency graph from saved, versioned module definition including typed input requirements, history timing parameters, expressions, ordered rules, outputs and non-expression references. Do not persist a competing graph. Validate cycles, missing references, ambiguous mappings and source revision; renaming a reference must update all supported sites or be blocked with a precise diagnostic.

**Slice C — primary authoring layout**: replace “drafts as buttons” with a draft picker/list, implement grouped outline and selected-step editor, unified view switch, stable inspector and no competing nested scrollbars. Share accessible components and spacing tokens. Preserve the current route, permissions, refresh/deep links and unsaved-change guard.

**Slice D — source inspector and connected trace**: turn nested preview/discovery controls into an optional *Data sources* panel with a compact per-input status summary in the main editor. Link the selected quantity to its adapter capability, original evidence, quality/time and downstream consumers. Do not expose raw provider connection IDs in portable saved contracts.

**Slice E — complex model parity**: first walk through the structured card calibration and ForeSite CURRENT-card POC as source/preview/design fixtures, then the ForeSite → WAMI role-composition POC; prove typed arrays/records, map/aggregation, conditions, code-backed step opacity, intermediate tracing, multi-provider authorization, artifact pinning, status/timeout failures and evidence invalidation. Only implement executable typed calculations, history/branching or provider services after runtime contracts and fixtures establish semantics; do not ship a visual-only feature that the worker cannot replay. Validate scenarios using Simulator→Gateway and then local licensed ForeSite/CygNet acceptance with evidence captured separately.

## Acceptance: demonstrate understanding, not just screenshots

- A complex draft with **no connection** can display multiple typed inputs, array transformations, an aggregate and two typed outputs, and correctly show what is and is not executable offline. The author can trace any declared output back to its explicit dependencies.
- With one selected imported well, the author can answer in one or two actions: *source provider? operation? quantity/schema? unit? timestamp? quality? reason missing? what consumes it?* ForeSite arrays preserve paired samples, statuses and capture times, not just scalar values. No hidden API guesswork.
- An authorized well lacking the expected quantity reports which input failed and why, without modifying the draft; switching assets cannot show stale responses from the prior asset/revision.
- Dependencies/Workflow ↔ Code/Expressions switches preserve selected node, unsaved work, scenario and trace; every visible edge is backed by an explicit step/port reference. Code-backed steps are visibly opaque unless instrumented. Controlled failures have explicit reason/status, not a blank graph.
- Test interactions at 1440, 1180, 1024, 820 and 768 CSS px; review-only mobile deep link; keyboard/touch interaction. A complex draft with 12+ steps, grouped collections and 3 independent provider roles must remain navigable at laptop and iPad widths. Screenshot diffs plus end-to-end tests cover desktop and tablet. Maintain a single principal scroll.
- Access policy remains Server-enforced, adapter executions remain Gateway-owned, and simulator/live preview are read-only. Production-ready claims require hosted Shadow execution and licensed provider evidence, not CI alone.

## Design guardrails

Use understated AutoPilot tokens, whitespace and typography; emphasize the *selected path*, not every connector. Keep provider troubleshooting on demand, avoid descriptive filler text and decorative badges. Refine the existing Claude mockups, but the complete trace and original timestamp/quality are the determining product improvements.