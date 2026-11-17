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

## Information architecture: one workbench, progressive disclosure

```text
Module title · Draft status/version                           Save   Validate   Submit
Workflow | Expressions                              Test context: None / Select sample well
─────────────────────────────────────────────────────────────────────────────────────────
Outline / Add                 Selected step (primary scroll)          Scenario & trace
Inputs                        ┌────────────────────────────────┐       Test case / Run
Parameters                    │ Input: Pump fillage            │       Expected vs actual
Calculations                  │ Value flow                      │       Deciding rule
Rules                         │ Source role → Quantity → Basis │       Input evidence
Outputs                       │ → Quality/age → Rule → Output  │       Missing/stale reason
                              │ [Configure fields / references]│
                              └────────────────────────────────┘
─────────────────────────────────────────────────────────────────────────────────────────
Reviewable: source requirements · validation findings · output contract · no field writes
```

- **Header**: identity, saved revision, unsaved/stale-evidence state, high-value actions. Put `Workflow | Expressions` in a compact view switch below the title; retain selected item, scroll context, test case, and evidence across changes. “IDE” is only an accurate label once the rich expression and rule editor exists. For now the UI may say “Expressions” while preserving the view state ID.
- **Outline**: grouped, named logical elements (Inputs, Parameters, Calculations, Rules, Outputs), not provider APIs or hardcoded starters. Include a compact **Add** menu and dependency hints. The blank state has a single clear **Add input** action.
- **Center**: edit the selected step and show its *actual* upstream and downstream relationships. For a simple rule, use a readable left-to-right flow summary; for larger modules, show a selectively expanded dependency graph **derived from the draft**, not independently editable edges. Clicking an upstream quantity navigates to its input definition; clicking a deciding rule navigates to that row. Avoid a full freeform n8n canvas as the mandatory experience.
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

## Interaction exemplar: threshold alert

1. Choose **New module → Quick start: Low fillage** (or blank → Add input). Input card is portable; no connection required.
2. Choose `Pump fillage` from reviewed supported quantities; specify `%`, current or average basis and data-quality policy. Do not convert arbitrary discovered names into a contract without a reviewed quantity mapping.
3. Select a sample well optionally. Asset compatibility checks the saved revision and shows source/path; click “Inspect details” for provider explanation. Failure is localized to the affected input.
4. Select the rule; insert `Pump fillage` as a typed reference, enter `< 40%`, choose recommendation. The *Connected to* section updates automatically from the same underlying draft references.
5. Run a deterministic scenario. Inspector shows measured/fixture input → matching condition → recommendation or skip, with expected vs actual and timestamp/quality evidence.
6. Save / Validate / Submit immutable revision. Review compares contract, scenarios and evidence; approval and downstream Logic assignment remain separate.

## Iteration and engineering boundaries

**Slice A — unlock existing green branches and baseline**: review then merge #618 → #619 → #620 in dependency order; verify CI/visual screenshots at each exact merged head, avoid false success claims. Capture 1440/1024/820/768 widths and fixed scenario fixtures. Create baseline screenshots of both perspectives and review screen.

**Slice B — source-to-consumer projection**: derive a canonical dependency graph from saved, versioned module definition including typed input requirements, history timing parameters, expressions, ordered rules, outputs and non-expression references. Do not persist a competing graph. Validate cycles, missing references, ambiguous mappings and source revision; renaming a reference must update all supported sites or be blocked with a precise diagnostic.

**Slice C — primary authoring layout**: replace “drafts as buttons” with a draft picker/list, implement grouped outline and selected-step editor, unified view switch, stable inspector and no competing nested scrollbars. Share accessible components and spacing tokens. Preserve the current route, permissions, refresh/deep links and unsaved-change guard.

**Slice D — source inspector and connected trace**: turn nested preview/discovery controls into an optional *Data sources* panel with a compact per-input status summary in the main editor. Link the selected quantity to its adapter capability, original evidence, quality/time and downstream consumers. Do not expose raw provider connection IDs in portable saved contracts.

**Slice E — advanced parity**: implement typed calculations, ordered conditional rules, outputs and history/fallback support only after the execution contract/fixtures establish semantics. Apply to POC VSD (25 → 23 Hz), Idle Time, Rod Buckling, and Rotaflex tests; do not ship a visual-only feature that cannot be reviewed and replayed by the worker. Validate scenarios using Simulator→Gateway and then local licensed ForeSite/CygNet acceptance with evidence captured separately.

## Acceptance: demonstrate understanding, not just screenshots

- New author with **no connection** can create an input → threshold → output, run a case and explain the result without provider-specific terms.
- With one selected imported well, the author can answer in one or two actions: *source provider? operation? quantity? unit? timestamp? quality? reason missing? what consumes it?* No hidden API guesswork.
- An authorized well lacking the expected quantity reports which input failed and why, without modifying the draft; switching assets cannot show stale responses from the prior asset/revision.
- Workflow ↔ Expressions switches preserve selected node, unsaved work, scenario and trace; all dependency links match the actual draft. Controlled failures have explicit reason/status, not a blank graph.
- Test interactions at 1440, 1180, 1024, 820 and 768 CSS px; review-only mobile deep link; keyboard/touch interaction. Screenshot diffs plus end-to-end tests cover desktop and tablet. Maintain a single principal scroll.
- Access policy remains Server-enforced, adapter executions remain Gateway-owned, and simulator/live preview are read-only. Production-ready claims require hosted Shadow execution and licensed provider evidence, not CI alone.

## Design guardrails

Use understated AutoPilot tokens, whitespace and typography; emphasize the *selected path*, not every connector. Keep provider troubleshooting on demand, avoid descriptive filler text and decorative badges. Refine the existing Claude mockups, but the complete trace and original timestamp/quality are the determining product improvements.