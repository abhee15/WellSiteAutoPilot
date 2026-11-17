# Authoring findings log (gaps found while mirroring and testing PRs)

Running list. Not committed to any repo. Share and prioritise later.
Format: ID, area, finding, evidence, found while testing, suggested direction.
Status: Open / May be in progress upstream (check before filing).

## A. Quantity discovery (PRs 614-616, 623)

**A1. Discovery is a fixed list, not dynamic by asset.** (Open, high)
- Discovery enumerates hard-coded operations (`history.pump-fillage`, `history.stroke-rate`, `TotalFluidRateInferred` daily) plus fixed allow-lists (`InferredPhaseRates` in 616, reported well-test fields in 615).
- Evidence: ForeSite well 776 `Surveillance/Well/DailyAverage` returns a `Units` map of 82 quantities. 21 had values in the last 7 days (THP, CHP, FBHP, DHPG, allocated/measured/inferred volumes, steam injection fields and others). The UI offers 1 (daily) or 2 (trend: PumpFillage, SPM).
- Found while testing: #614, #616, #623 in visible Edge.
- Direction: read the well's `Units` map and populated values, and return them dynamically. `CygNetTrends` is documented as history for any mapped quantity, but the adapter registers only `CurrentPumpFillage` and `CurrentSPM`.

**A2. Well with no test on file gives the wrong message.** (Open, medium; PR 615)
- `WellTests/GetLatestWellTestData/{id}` returns an empty 200 body for wells 776 and 732. The UI shows "Could not discover quantities for this well." instead of "no reported well test".
- Evidence: Edge on RPOC_00016+9077 with source "Well test". Well 715/RPOC_0412 works.
- Direction: treat an empty body as "no test reported".

**A3. Positive path of phase-rate discovery unverified.** (Open, low; PR 616)
- No local well had inferred oil/gas/water rates in the window, so only unit tests cover it.

**A4. CygNet has no discovery, history or production-day reads.** (Open, medium)
- UI shows "This source has no registered read for the selected well or search window." Generic and not actionable.
- Related: CygNet preview failures show only "Live preview could not be completed".

## B. Sample cases (PR 605-ish onward, seen in 622-626)

**B1. Sample cases are fixed and not authored.** (Open, high)
- Built in `GuidedAuthoringPage.tsx`: threshold uses `boundary-8`, `boundary`, `null`; pressure uses 4 casing/tubing pairs. A draft does not store them. Users cannot add, edit or name cases.
- They are the evidence reviewers see on submit (server accepts 1-50).
- Direction: author and store named cases per draft, including expected outcomes. Allow cases from real provider data.

**B2. Live preview is not submission evidence.** (Open, medium)
- "Run live preview" uses real well readings but submission uses only the synthetic cases. Consider an option to attach live evidence to a submission.

## C. Complex (non-guided) modules (PRs 624-626)

**C1. No way to author complex modules in the UI.** (Open, high)
- Read-only contract view only. The create endpoint takes no manifest, so complex drafts can't be created through the product. I could only see the view by overriding the list response in the page.
- Direction: define the authoring path (upload a manifest, IDE editing or a template).

**C2. Outputs lack requirement detail.** (Open, low-medium)
- Inputs expand to a requirement panel (id, role, access, capability, max age, schema, fields). Outputs list names and types only. Only the first input (SurfaceCards) was clickable in my run, and I did not test the others.

**C3. Contract panel on ordinary drafts reads oddly.** (Open, low)
- Plain pump-fillage draft shows "Declared data contract - 1 inputs - 0 outputs", and the count text is not pluralised ("1 inputs").

**C4. No sample cases, live preview, Save or Submit for complex modules.** (Open, expected)
- Only inspection is possible. See PR 628 (bounded runtime-backed trace) for the direction.

**C5. "Shape not declared" shown for simple scalar inputs.** (Open, low-medium)
Seen on the Pressure Differential Test draft: CasingPressure and TubingPressure (psi) both read "Shape not declared". Scalar ports have only an id, quantity and unit and no valueSchema, so GuidedModuleContract.shapeLabel() falls through to the label. The shape is known (a number in the port's unit), so the label implies missing data that is not missing. Suggest showing the quantity and unit for scalars and keeping the label for complex ports that really lack a schema.

**C6. "No outputs are declared" shown on a draft that has a rule.** (Open, low-medium)
Same draft: the panel says "0 outputs / No outputs are declared in this manifest" while the Rule below produces a recommendation ("High casing-to-tubing pressure differential") in the sample cases. Guided drafts keep the outcome in the rule, not in the manifest outputs, so the contract panel contradicts the page. Related to C3.

## D. Verification gaps (our side)

- `visual-accessibility.mjs` and `tools/ci` scripts are not mirrored because the P4 copies diverged. No browser accessibility check has run on the new layouts (622-626).
- Flaky vitest tests under load: `connection-inventory`, `editable-logic-drafts`, `control-actions` and one guided-authoring test. They pass alone. Consider raising the 15 s timeout or splitting `connection-inventory`.
- "New module" starter flow (PR 622) not exercised in my script.

## E. Other open items (not PR findings)

- Server auto-update stopped 11/13 to 11/16, cause unknown. Script logs nothing if the share read fails. Suggest a try/catch that logs the error.
- ASCII en-dash fix on branch `fix/foresite-ascii-descriptors` is on hold.
- CygNet "discovery failed" on the user's server waits for the Gateway log location.
- ForeSite probe findings are in `foresite-probe\` and `foresite-adapter-gaps-issue.md`.
