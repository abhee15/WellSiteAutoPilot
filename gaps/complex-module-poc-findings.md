# POC: complex-module authoring (branch poc/authoring-complex-modules, not committed)

Script: D:\WellSiteAutoPilot\authoring-poc\poc\complex-modules\run-api-tests.ps1 (15 PASS, 2 INFO, 0 FAIL)

## Server (contract-only drafts, #635) - works as designed
- Create/read/list/edit; stale revision -> 409.
- Contract-only -> rule draft switch -> 400 guided_draft_kind_immutable.
- Malformed/oversize manifest -> 400.
- Manifest with commandRequirements -> 400 GUIDED_CONTRACT_COMMANDS_UNSUPPORTED (earlier "fail" was my wrong field name `commands`).
- Preview / submit / live-preview -> 400 GUIDED_DRAFT_LOGIC_UNSUPPORTED.
- Zero inputs -> 400 module_contract_invalid.

## Gaps
- P1: asset-compatibility returns Unsupported / GUIDED_LIVE_INPUT_UNSUPPORTED for structured inputs (cards, calibration) even on ForeSite RPOC_0413, which has stored cards (surface-cards-preview returns 2 cards, quality Unknown). Compatibility check does not understand structured inputs.
- P2: quantity-discovery returns 400 for card-only contracts (no scalar source); no explanatory UX.
- P3: UI for a contract-only draft is view-only: no Save, Submit, Run sample cases or Run live preview buttons (count 0). The draft can be inspected but cannot be edited, previewed or submitted from the UI.
- P4: No UI path to create a contract-only draft (API only). Not verified beyond the "New module" button; the click flow timed out before reaching it.
- P5: Contract view shows "Internal steps not recorded" and no input-to-output dependencies; needs recorded execution evidence.
- P6: No delete endpoint for drafts; test drafts persist in local DB (~3 contract-only + "POC complex renamed" x2 shown in list). Duplicate draft names are allowed and indistinguishable in the picker.
- P7: Card quality reported "Unknown" for stored cards while the input declares allowUncertainQuality=No; unclear if a live run would reject them.
- Positive: ports render schema, max items, fields; no horizontal overflow at 760px.

## Not verified
- IDE tab and New module flow in UI (click timed out).
- Any execution of a complex module end to end (server rejects by design).

## Live verification of #636 (CL 1108673), Edge visible, 2026-11-17
- Works: New module -> "New contract-only module" starter, Add input/output, per-port editor (quantity, unit, value type Number/Text, access Current/History, max age, uncertain quality, provider capability), Save-only toolbar, IDE tab, no overflow at 760px. Input+output draft saves ("Draft saved.", HTTP 200).
- P8 (bug): the blank starter and any draft with 0 inputs or 0 outputs cannot be saved. POST /guided-module-drafts -> 400 MODULE_CONTRACT_INVALID "Structured modules require .NET, 1-64 inputs and 1-64 outputs." Cause: Program.cs ReadGuidedDraftAsync calls LogicModuleManifestReader.ReadAsync, which validates strictly (allowEmptyPorts=false); the #636 allowEmptyPorts path is only used by GuidedContractDraft. The PR text says zero ports may be saved while building. Unit checks pass because they bypass the route.
- P9 (UX): the 400 above surfaces only as "The operation could not be completed. Check access or reopen the latest draft and try again." - misleading; the real reason (add at least one input and output) is not shown.
- P10: new inputs default to capability structured.current, which adapter read rules cannot serve (addressed by #637, not yet verified live).

## Live verification of #637 (CL 1108674), Edge visible, 2026-11-17
- P10 resolved: the quantity field offers registered scalar quantities from the adapter operations registry (AveragePumpFillage [ForeSite], CasingPressure [CygNet], MotorAmps [CygNet], PumpFillage [ForeSite, CygNet], RodLoad [CygNet], TubingPressure [CygNet]); choosing one fills the unit (AveragePumpFillage -> %) and sets the capability to engineering.current (read-only). An input saved (HTTP 200).
- An unregistered quantity (including the default "Quantity1") shows "No registered source provides this quantity yet..." and Save draft is disabled. No overflow at 760px.
- P11 (UX): a new input starts with the default quantity "Quantity1", which can never be saved, so every new input starts in an error state; consider starting empty with a prompt. Registered quantity list shows no units until chosen.
- P12: the registry lists only quantities a configured adapter registers; structured inputs (cards, calibration) have no picker, so card contracts still cannot be authored from the UI (see P1/P2).
- Not verified: Unit/capability behaviour for CygNet-only quantities and saving after switching quantity on an existing saved draft.

## Live verification of #638 (CL 1108675)

Run on the P4 tree, Negotiate auth, visible Edge, ForeSite asset RPOC_0413.

Works:
- Contract-only draft with one registered scalar input (AveragePumpFillage) saves (HTTP 200).
- "Preview with a sample asset" is available; asset search finds RPOC_0413.
- asset-compatibility returns Satisfied for the scalar input (reasonCode null).
- quantity-discovery returns supported=true (PumpFillage %, SPM).
- The History controls (window, duration, historical quantity) appear.
- No Run button on a contract-only draft; the IDE tab shows the contract editor and sample-asset inspection together; no overflow at 760px.

Not verified: history read of a selected quantity; card/calibration inputs (P1/P2 remain open, since only a scalar input was used).

New observations:
- P13: the output port also defaults to the unsaveable name Quantity1 (same as P11).
- P14: the IDE tab button does not respond to a role=button click in automation (found by text only); check its accessible role.

## Breadth testing of #638 (CL 1108675): input kinds, value types, asset device types

Method: API matrix against the live stack (P4 tree, Negotiate, ForeSite + CygNet connections), ~126 ForeSite assets, drafts created per variant, then asset-compatibility, quantity-discovery and history-preview per asset. Device types came from the ForeSite probe (LufkinSam, WellPilotRPOC, WFordKS/ESP, AEBEAM, WFTA1K). Reference-module inputs came from lm-blueprints (Idle Time on RPOC, Target Fillage on VSD, Rod Buckling on Lufkin).

Results summary:
- AveragePumpFillage (scalar current): Satisfied 22 of 72 assets, Missing 46, 404 x4. By device: LufkinSam 2/4, WellPilotRPOC 4/8, WFordKS/AEBEAM/WFTA1K 0.
- Structured cards and calibration: Unsupported on every asset of every device type (P1 confirmed at scale). Stored-card read still works on RPOC_0413 (2 cards, quality Unknown), so inspection and compatibility disagree.
- Every reference-module input other than PumpFillage/AveragePumpFillage is Unsupported on all device types: SPM, AverageSPM, CyclesYesterday, RunTimeYesterday, StrokesYesterday, InferredProductionYesterday, IdleTime, MinSpeed, MaxSpeed, TargetFillage.

New gaps:
- P15 (design question): the author cannot choose or pin the provider or the operation (surveillance, daily averages, well trend, well test) for an input. The registry lists PumpFillage as ForeSite and CygNet, but the capability is filled in read-only and "Registered by" is only a label. Anchored "yesterday at clock time" inputs have no fields.
- P16: the registry has 6 quantities. The three reference modules need 10+ more (see summary). Discovery returns SPM (observed) but the UI only says "Add this quantity to the draft to preview its trend", and SPM cannot be added because it is not in the registry.
- P17: the server accepts what the UI blocks: unregistered quantity, wrong unit (kg for a % quantity), text value type on a numeric quantity, structured.current on a scalar, two inputs for the same quantity. All return HTTP 200 on create. Compatibility then reports Missing on assets without data and reports the mismatch (EXECUTION_INPUT_QUANTITY_MISMATCH) only where data exists, so the unit error is masked.
- P18: history-preview rejects any input that has a valueSchema (GUIDED_HISTORY_INPUT_UNSUPPORTED), and every UI-created contract input has one, so the trend preview cannot work for contract-only inputs. A History-access scalar input is also Unsupported in compatibility.
- P19: Unsupported does not say whether the quantity is unregistered, the asset's connection does not provide it, or the access mode is not supported. All map to GUIDED_LIVE_INPUT_UNSUPPORTED.
- P20: 4 of 72 (then 9 of 126) assets answer 404 GUIDED_LIVE_SOURCE_UNAVAILABLE on compatibility (assets with no parent device, for example Asset Test, Demo, AR-REC, Analytics_Wells). The UI message for this was not checked.
- P21: the repo samples (three CygNet checks, authoring dotnet and python) are V1 manifests with entry points and cannot be loaded as contract-only drafts (GUIDED_CONTRACT_INVALID). There is no way to start a contract from an existing manifest.
- P22 (environment): all 126 assets searched belong to the ForeSite connection. No CygNet or SJVKRP assets were found, so the CygNet-registered quantities (CasingPressure, MotorAmps, RodLoad, TubingPressure) could not be checked against a live asset.
- Age 0 is rejected (module_data_max_age_invalid) while a null age is accepted; the UI shows only the generic message (P9).

Not tested: ESP/PCP quantities (no fillage by design), commands, structured inputs other than cards and calibration, large assets lists in the UI, any run of the module.

## CL 1108676 (#639, P8 fix) mirrored to Perforce

- P8 (blank or one-sided contract-only drafts rejected with 400 MODULE_CONTRACT_INVALID) is fixed in Perforce from this CL. CL 1108673 (#636) carried the bug until now.
- Checked: Server Release build and ExecutionContractV2Tests pass on the P4 tree. The fix was verified live by API on the fix branch before it merged (blank manifest POST returned 200, revision 1). It was not re-run through the UI on the P4 tree.
- Mirror note: Program.cs merged with existing local differences (the P4 copy is 6 lines shorter than GitHub: it lacks the Audit and Changes usings). This difference predates this CL; worth a diff review of P4 Program.cs against develop.
- No new product gaps from this CL.

## Live verification of #640 (CL 1108677)

Visible Edge, maximized, P4 tree, Negotiate auth.

Works:
- Rule drafts show "Declared data contract · 1 input · 0 outputs" and "2 inputs · 0 outputs" (plurals correct).
- Scalar ports show "Number" and the unit, not "Shape not declared".
- The no-outputs text reads "No typed outputs are declared. The rule below still produces the recommendation."

Not seen: the panel does not render on contract-only drafts (the contract editor replaces it), so the new wording is only visible on rule drafts. Structured-port shapes (cards) were not opened in this panel.

New observations:
- P23: the draft picker now lists 30+ drafts with duplicate names (several "CL638 sample check", "Live check pressure differential"). With no delete (P6) and no search, the list becomes unusable. The extra drafts here came from the breadth tests.

## CL 1108717 (#641, named sample cases) - live check 2026-11-18
Visible maximized Edge on draft "Sri Pump Fillage Test" (threshold 50). Case editor lists 3 default cases (42, 50, blank) plus a 4th added case.
- Add case, name, reading and expected outcome (Rule triggers) work. Run is disabled until the draft is saved; after save, 10% gives "Low pump fillage" and 250% gives "No recommendation" with "Does not match the expectation. Observed: NONE."
- Blank reading shows "Required reading unavailable". The number input refuses text.
- **P24** Sample-case reading accepts values outside 0-100 for a percentage (250 saved and evaluated).
- **P25** "Submit for review" is enabled while a case has a failing assertion; refusal happens only server-side. Not exercised (submit not run).
- Not verified: submission rejection, IDE-tab editing of cases, full vitest suite, contract-only drafts (no case editor expected).

## CL 1108729 (#642, unsupported-input reasons, empty new ports) - live check 2026-11-18
Visible maximized Edge. Full vitest: 663/665 first run (guided-authoring and connection-inventory timed out under load); both pass alone (24/24 with guided-contract-editor).
- New input and output ports start with an empty quantity. Save stays disabled until the port is filled.
- Preview reasons: RPOC_0413 "AveragePumpFillage - Meets requirement"; AR-ESP2 and AEPOC_0001 "Reading missing - Foresite measurement missing" (previously a generic code).
- **P26** A port whose quantity is not registered (e.g. SPM, NotARegisteredQuantity) gets no capability or unit, and Save stays disabled with no stated reason; other ports are fine. The user cannot tell which port blocks save.
- Not verified: wrong-capability, wrong-value-type, access-mode and provider-mismatch reasons (only the "reading missing" path was reachable).

## CL 1108751 (#645, actionable save/submit refusal messages) - live check 2026-11-18
Visible maximized Edge, draft "Sri Pump Fillage Test" (saved case 'Edge below boundary' expects trigger at 250%, observed none).
- Submit is disabled until "Run sample cases" is run, then enabled. Submit returned 400 guided_sample_expectation_failed and the page showed "A sample case did not produce its expected outcome. Fix the rule or the expectation, save, and run the cases again." P25 confirmed: submit stays enabled with a failing assertion, but the refusal is now clearly explained. Message does not name the failing case (server detail names 'Edge below boundary').
- Not verified: revision conflict, already submitted, invalid cases, no rule, max age, duplicate input, unknown role, value schema messages.

## CL 1108755 (#643, structured inputs from registered provider schemas) - live check 2026-11-18
Server Release build, V2 tests, ForeSite adapter tests (live ForeSite check skipped, no FORESITE_BASE_URL), tsc (after client regen), eslint, guided-contract-editor vitest (4/4) and 379 guards passed. Full vitest not run.
Visible maximized Edge, new contract-only module:
- One extra button "Add structured input: SurfaceCards" (the only registered structured quantity); port shows "This structured shape is kept as saved. Editing structured shapes is not available yet." Save works.
- Compatibility: AR-ESP2 -> "Reading missing - Foresite measurement missing"; RPOC_0413 -> Rejected "An input's data quality was not good enough" (EXECUTION_INPUT_QUALITY_REJECTED), even after ticking "Allow uncertain quality" and saving.
- **P27** Cards on RPOC_0413 (stored cards report quality Unknown) are rejected by quality, and "Allow uncertain quality" does not admit them, so no asset reaches Satisfied for a structured card input in this environment. Unverified whether Unknown quality is meant to be excluded or the checkbox did not persist; needs a follow-up.
- Not verified: structured input from another provider (only ForeSite registers one), editing structured shapes (intentionally unavailable), a Satisfied structured result.

## CL 1108756 (#644, delete unsubmitted drafts) - live check 2026-11-18
Skipped (diverged in P4): tools/ci/check_api_authorization.py, tools/ci/verify_grant_administration.py, tests/ci/test_check_api_authorization.py (so the API-surface guard and grant-administration e2e changes are NOT mirrored/verified here). Program.cs MERGED, no conflict markers.
Checks: server Release build, V2 tests, tsc (after client regen), eslint, guided-authoring and guided-contract-editor vitest (16/16), 379 guards. Full vitest not run.
Visible maximized Edge: "Delete draft" shows "Delete this draft? ... cannot be undone. Submitted drafts are kept as review evidence"; Keep leaves 26 drafts; "Yes, delete" sends DELETE ...?expectedRevision=1 -> 204, list drops to 25, "Draft deleted.", editor resets to Untitled module.
API: stale revision -> 409 GUIDED_DRAFT_REVISION_CONFLICT (draft intact); missing expectedRevision -> 409 (not 400); unknown id -> 404 guided_draft_not_found.
- P6 and P23 (no delete) are addressed for the UI. P23's search/duplicate-name part is NOT addressed: the picker still lists duplicate names unsorted; deletion is one at a time.
- **P28** Missing expectedRevision returns 409 conflict, not a validation error (minor).
- Not verified: refusal for a submitted draft, non-owner refusal, audit record.

## CL 1108757 (#646, empty well test = no test; contract panel direction/unit) - live check 2026-11-18
Checks: server Release build, ForeSite adapter tests (live ForeSite check skipped), tsc, eslint, guided-module-contract vitest (6/6), 379 guards. Full vitest not run.
Visible maximized Edge:
- C2: rule drafts show "Direction: Input (read by the module)" and Unit (PumpFillage %; CasingPressure psi; TubingPressure psi) for each selected input. Output direction wording not seen (no rule draft has outputs; contract-only drafts have no panel).
- A2: Well test source on RPOC_0413 returns evidenceStatus NoTest and the UI says "ForeSite returned no latest well test for this well." AR-ESP2 and AEPOC_0001 return reported tests with quantity lists.
- **P29** Well-test panel shows raw values: ISO timestamps with +00:00 offsets, a bare "Test status 2103" code, and every reported quantity has unit null ("Eligibility and units are unverified").
- Not verified: the empty-response path against a real empty ForeSite payload (adapter unit test only); RPOC_0413's NoTest is whatever ForeSite returned today.

## CL 1108758 (#647, offer every populated daily quantity with a provider unit) - live check 2026-11-18
Checks: server Release build, ForeSite adapter tests (live ForeSite check skipped), V2 tests. No web files in this PR. Description follows the type(scope) convention, no gap IDs.
Visible maximized Edge, Quantity source = Daily averages on "Sri Pump Fillage Test":
- RPOC_0413: 18 quantities, each with a unit (e.g. TotalFluidRateInferred STB/d, AllocatedGasVolume Mscf, FBHP psia, THT F).
- AR-ESP2 (ESP): 35, including MotorAmps (A), MotorFrequency (Hz), MotorTemperature (F), MotorVolts (V), PIP/PDP (psia), Vibration (g), ChokeDiameter (1/64in) -- device-specific, so the list now follows the well rather than a fixed oil/gas/water set.
- AEPOC_0001: same 18 as RPOC_0413.
- **P30** Order is not sorted: TotalFluidRateInferred is first, then the rest alphabetical, so the list looks inconsistent. Minor.
- Not verified: the 128 cap (no well near it), quantities with no unit being omitted, behaviour with an empty Units map.

### P31 - Daily averages: no way to read the value of a discovered quantity (user-reported)
After #647 the Historical quantity list offers every populated daily quantity (e.g. MotorAmps, MeasuredGasVolume), but the read button appears only for TotalFluidRateInferred. Any other choice shows 'The daily series preview supports inferred total fluid rate.' and no button. Cause: GuidedQuantityDiscovery canPreview is hard-coded for DailyAverages, and the production-days preview request/reader (ForeSiteDailyAverageContracts.Quantity) has no quantity parameter. Fix needs a quantity field on GuidedProductionDaysPreviewRequest, the adapter reader and the UI gate. Until then the dropdown offers choices that cannot be previewed.


### P32 - Quantity source labels expose provider operation names (user-reported)
The Quantity source select lists Surveillance, Daily averages, Well trend and Well test (GuidedQuantityDiscovery sources array). These are ForeSite operation names, not author concepts. Proposed direction: present Current values (surveillance) and History values (backed by Well trend), with the quantity list always fetched dynamically for the selected asset and source, so authors never choose by provider capability. Related to P30 (unsorted list) and P31 (daily quantities not readable); the source model needs one consistent read path per quantity.


### P33 - Well-specific wording limits the authoring model to wells (user-reported)
Labels such as Well trend, Well test and 'imported wells' in the live preview notice imply the asset must be a well. Authoring language should be asset-neutral (Current values, History values, Latest test) so modules can later target other asset types (pads, facilities, compressors) without renaming. Provider-specific names stay inside the adapter layer. Extends P32; affects labels in GuidedQuantityDiscovery, GuidedLivePreview and the localization keys guided.* (e.g. the preview support notice 'imported wells').


### P34 - Surface cards should be a quantity source, and are RRL-only (user observation, design open)
Today cards are a separate 'Read stored surface cards' button outside the quantity source model. Idea: make Cards a quantity source so the author can request (a) the latest card, optionally of a given card type, or (b) an array of cards over a date range. Cards apply only to RRL (rod lift) wells, not every well type, so the source and its options must be offered per asset/lift type, and unsupported assets should say why. Design of the authoring screen is deliberately undecided; to be settled together with P31, P32 and P33 (one source model: current values, history values, cards, tests, each with asset-aware availability).


### P35 - Live preview panel layout, button sizing and data presentation (user-reported, screenshot)
Observed in 'Preview with a sample asset':
- Find assets is a large bordered box stretched to the height of the label+input row and centred, much bigger than the field it serves; it is not aligned with the input baseline.
- Button sizing is inconsistent: Find assets is a big block, Run live preview is full width, Read stored surface cards is a small left-aligned button. No clear primary/secondary action hierarchy.
- Panel content is indented relative to the rest of the form, and the two intro paragraphs have large vertical gaps between them and overlap in meaning (live readings are preview only / live preview supports imported wells).
- The asset name input shows a tinted autofill-style background while other fields are plain.
- Compatibility result is a terse info box with a bullet 'PumpFillage - Reading missing - Cygnet point not found'; internal provider wording, no per-input status layout, no next step.
- Sample asset option text differs between runs ('REMDEV / RPOC_00016' vs 'AR-Test / RRL / Unspecified device / RPOC_00016+9077'), so the same asset is described inconsistently.
- Quantity source, quantity and read actions are stacked with no grouping, so it is unclear which button acts on which selection.
The panel needs a layout/presentation pass once the source model (P31-P34) is decided; recorded as an observation only.


## SME walkthrough of the preview panel (visible Edge, P4 tree with #647; assets RPOC_0413 RRL and AR-ESP2 ESP)
Observations only, no changes made.

### P36 - Source values are listed but never shown
Surveillance lists 'AveragePumpFillage (%) - Reading returned' and 'PumpFillage (%) - Reading returned' but not the value, time or quality of the reading. Well test lists Oil, Water, Gas, TotalFluid, StrokePerMinute as 'Reported on this test' with no numbers. An author exploring what a source gives has to guess; the value should be visible next to the name (or on demand).

### P37 - Contradictory availability messages for the same input
RPOC_0413: the compatibility box says 'PumpFillage - Reading did not meet requirements - An input was too old' while Surveillance says 'PumpFillage - Reading returned'. The quantity list ignores the age/quality requirement the module contract sets, so 'returned' looks like success.

### P38 - Applicability is not asset-type aware
AR-ESP2 (ESP) is offered PumpFillage and SPM under Well trend ('Provider read unavailable'), the pump-fillage surveillance rows ('Reading missing'), and 'Read stored surface cards' (clicking gives 'The provider returned no stored surface cards'). Rod-lift quantities and cards should not be offered, or should be marked not applicable, for non-RRL lift types (see P34). Conversely the ESP daily list does include ESP-only quantities (MotorAmps, Vibration), so the provider knows the lift type.

### P39 - Developer caveats shown to authors
Surveillance shows 'Surveillance quantity applicability awaits verification against the installed ForeSite version' and Well test shows 'Eligibility and units are unverified'. These are build-time notes, not guidance an author can act on.

### P40 - Quantity catalogues differ by source with no explanation
RPOC_0413 Well trend offers only PumpFillage and SPM; Daily averages offers 18 different quantities; Surveillance offers AveragePumpFillage and PumpFillage. The same physical quantity (pump fillage) is not offered under the daily source and names differ between sources. The author cannot tell which source to use for a given need (relates to P32).

### P41 - Surface card result is hard to interpret
Result is 'CURRENT 1 - 10/16/2022 ...' and 'FULL 2 - 10/16/2022 ...' with 'Position 0-124.5 in - Load 9088-21928 lb - 112 paired points' and 'Quality Unknown'. Card types are shown as raw names with unexplained numbers, the 4-year-old timestamp is not flagged as stale, units/axes are not labelled as a card, and there is no plot, so the author cannot judge whether the card is usable.

### P42 - Placeholder text leaks into asset labels
Sample asset options read 'Not Mapped / RRL / Unspecified device / RPOC_0413'. 'Not Mapped' and 'Unspecified device' are internal placeholder values, and the format (site / lift / device / name) differs from other runs ('REMDEV / RPOC_00016'). The select also keeps a 'Select an asset' entry when there is only one match.
