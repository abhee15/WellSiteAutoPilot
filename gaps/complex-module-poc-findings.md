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

## CL 1108759 (#648: history preview for numeric inputs, no-source message) - live verification
Visible Edge on 'Sri Pump Fillage Test' (P4 tree with #648, stack restarted after server change).
- P20 addressed: choosing Analytics_Wells (an asset with no single active source) returns 404 guided_live_source_unavailable and the panel says 'This asset has no single active imported source, so it cannot be checked. Choose an asset that comes from an enabled connection.' No Retry button is shown. The Run live preview button is still offered for that asset though (see P43).
- P18 addressed: RPOC_0413, Well trend, PumpFillage (%), 'Load history' returns 8 half-hourly samples (99.6 %, quality Unknown), shown in local time.

### P43 - Run live preview and source pickers stay enabled for an asset that cannot be checked
After the no-source message the Run live preview button, the quantity source select and Read stored surface cards remain available; every one will fail. They should be disabled (or hidden) once the asset is known to be unusable.

### P44 - History result contradicts the compatibility result
For RPOC_0413 the compatibility check says PumpFillage 'Rejected / EXECUTION_INPUT_STALE' ('An input was too old') while Load history shows readings up to 3:21 PM, two minutes before the check, all with quality 'Unknown'. The author is not told what 'too old' is measured against (module max age vs the 30-minute sample interval) or that quality Unknown is what is being rejected. Related to P37.

### P45 - History button label and placement
The read action is called 'Load history' for Well trend but 'Read last 7 days of production' for Daily averages, and appears between the selectors and the 'Search window' line. Naming and position are inconsistent (relates to P32/P35).

Not verified: history for a numeric input declared with a schema other than a plain number (rejected by design), site-local time anchor on this CL, and the no-source message for an inactive or unauthorised asset (only the no-source case was reached).

## CL 1108777 (#649) verification

Scalar V2 current inputs through the structured worker path. Mirrored CLEAN (9 files, 2 new). Server and Worker.DotNet Release builds pass, ExecutionContractV2Tests pass including the new scalar runtime checks, added lines are ASCII. Server code changed, so the AppHost was rebuilt and the stack restarted (first start failed on a transient kubeconfig file lock, retry was healthy). No UI change in this PR. Not verified live: a scalar current read through a running worker on a real ForeSite asset; covered only by the contract tests. No new gaps.


## CL 1108780 (#650, source hints) - live verification and SME review

Verified live in visible Edge (P4 tree with #650, contract-only draft "GAP variant: scalar current number (AveragePumpFillage)"):
- The Read from provider and Read with operation fields appear per input. Providers listed: Provider Simulator, ForeSite, PetroVisor, CygNet. ForeSite operations: Average pump fillage, Daily inferred total fluid rate, Active well alarms, Read well details, Saved setpoints, Latest reported well test. CygNet: Facility points, Active point alarms. Simulator and PetroVisor: none.
- Choosing a provider, then Only run with this source, shows the warning. Save and reload keep provider and the required flag. Clearing the provider disables the required checkbox.
- Required CygNet against the ForeSite asset RPOC_0413 gives "Unsupported for this asset - The module requires a different provider than this asset uses". Required ForeSite plus Average pump fillage against the same asset passes the hint (the reading then fails as "too old", the known P37 data-age issue).
- Server build, V2 tests, tsc, eslint, vitest (guided-contract-editor 5/5) and the 379 node guards pass. Added lines are ASCII.

### P46 - The operation list offers operations that cannot serve the input
For an AveragePumpFillage input the ForeSite list shows Active well alarms, Read well details, Saved setpoints and Latest reported well test. The filter keeps "provider-level reads not tied to one quantity", so an author can require an operation that can never produce the input. The same list for CygNet shows Facility points and Active point alarms. Suggestion: list only operations that can serve the quantity and access type (current, history, daily, test), and say so when none can.

### P47 - Providers with no usable operation are still offered
Provider Simulator and PetroVisor appear in the provider list but have no operation for this quantity. The contract panel says "Registered by: ForeSite" yet CygNet can be set as required and saved, which can never match any asset. Suggestion: mark or disable providers that do not register the quantity, or warn at save time that no known provider serves the input.

### P48 - The mismatch reason does not name the providers
"The module requires a different provider than this asset uses" does not say which provider is required, which one the asset uses, or which operation was wanted. An author cannot act on it without opening the connection. Suggestion: "Requires CygNet; this asset reads from ForeSite" and the equivalent for operation.

### P49 - Layout of the source fields
The fields were added to the end of the input row and look unfinished:
- The checkbox labels (Allow uncertain quality, Only run with this source) sit far from their checkboxes and the labels float on the baseline of other fields.
- The info notice sits in the grid beside the field instead of under the checkbox it explains.
- The Remove button is crowded against the operation select.
- "Only run with this source" is ambiguous. "Required" or "Do not run on other providers" is clearer, and it does not say that the module stops being portable.
Suggestion: a separate "Where it is read from" group per input, with the portable default stated ("Any provider that serves it - recommended").

### P50 - Hint parameters cannot be authored
A hint holds an optional parameters value but the editor always saves it empty. Choices such as which card type, how many days of history, or which daily field have nowhere to go. This ties to P34 (cards as a quantity source) and P32 (current versus history values). Suggestion: parameters derived from the operation's declared options, shown after the operation is chosen.

### P51 - Provider names in a portable module
A hint stores a provider key (for example foresite) inside the draft. This is explained as not part of the portable manifest, but nothing shows the author which parts of a draft are portable and which are local. It also conflicts with the P33 direction to avoid provider-specific wording. Suggestion: show a "Portable / Provider-specific" indicator on the module summary and on export.

## CL 1108781 (#651, provider daily field as scalar input) - live verification and SME review

Verified live against ForeSite (P4 tree with #651, stack restarted on the new integration code). Contract-only drafts (engineering.daily, number schema, quantity = ForeSite daily field) were created through the API and checked against RPOC_0413 (RRL), AR-ESP2 (ESP) and AEPOC_0001:
- TotalFluidRateInferred, unit STB/d, uncertain quality allowed, max age 2 days: Satisfied on all three assets.
- Same with uncertain quality not allowed: Rejected EXECUTION_INPUT_QUALITY_REJECTED (the daily reading is always Uncertain).
- Unit declared as bbl: Rejected EXECUTION_INPUT_QUANTITY_MISMATCH.
- Max age 1 hour: Rejected EXECUTION_INPUT_STALE (the reading is stamped with the production day end).
- RunTimeYesterday (not in these wells' daily record): Missing FORESITE_MEASUREMENT_MISSING on all three.
- CHP: Satisfied on AR-ESP2 (ESP), Missing on RPOC_0413 and AEPOC_0001.
- No unit declared (AllocatedOilVolume): Satisfied on all three.
- Server and Worker builds, V2 contract tests, ForeSite adapter tests (live ForeSite check skipped, no FORESITE_BASE_URL) and the added-lines ASCII check pass. No web changes in this PR.
- Not verified: the worker run path with a real module (live preview refuses contract-only drafts), so the reading value itself is not shown anywhere yet (see P36).

### P52 - A wrong unit is reported as a quantity mismatch
The quantity matched; only the unit differed (declared bbl, provider STB/d), yet the reason is EXECUTION_INPUT_QUANTITY_MISMATCH. One code covers two different faults. Suggestion: separate EXECUTION_INPUT_UNIT_MISMATCH and include both units in the message ("declared bbl, ForeSite reports STB/d").

### P53 - Missing field does not say why
FORESITE_MEASUREMENT_MISSING is the same whether the field is not recorded for this lift type (CHP on an RRL well), the day has not closed, or the lookback of 4 days has no record. It also carries a provider name in the code, unlike the provider-neutral codes beside it. Suggestion: neutral reason such as "No finished production day in the last 4 days reports this field for this asset", and show which lift types report it.

### P54 - Freshness for a daily value is expressed in seconds
The reading is stamped with the end of the production day, so it is already up to a day old when read. An author who copies the usual 60 second or one hour maximum age gets STALE on every asset, and the blank default accepts any age. Suggestion: for a daily input, express freshness in production days ("latest finished day, no older than 2 days") and default it.

### P55 - Daily readings need uncertain quality, but the default rejects them
Every daily value is Uncertain, so a new input with the default setting (uncertain not allowed) fails on every asset with QUALITY_REJECTED. The author has to guess the cause. Suggestion: when the daily capability is chosen, set and explain "Allow uncertain quality" automatically, or say so in the reason ("daily values carry no verified quality").

### P56 - A contract with no unit accepts any unit
The no-unit draft is Satisfied on all assets, so a module can silently receive STB/d, Mscf or psia values as plain numbers. The unit travels with the reading but the module author is not told to check it. Suggestion: require a unit for numeric inputs, or warn on save when none is declared; ties to P5 and P42.

### P57 - Daily field names are provider-specific
The quantity is the provider's own daily field (TotalFluidRateInferred, AllocatedOilVolume, CHP). Another provider's name for the same field will not match, so a module reading daily production is not portable. The registry covers current quantities only. Suggestion: a small set of canonical daily quantities (oil, gas, water, fluid volumes; run time; cycles) mapped per provider; ties to P33 and P51.

## CL 1108782 (#652, add a discovered production-day field as a daily input) - live verification and SME review

Verified in visible Edge on the contract-only draft "GAP variant: scalar current number" with sample asset AR-ESP2 (ESP):
- Choosing Quantity source = Daily averages lists the 30 daily fields with units straight away.
- Selecting CHP and pressing "Use as input (latest finished day)" adds an input CHP: unit psia, number, access Current, max age 172800 s, uncertain quality allowed, capability engineering.daily. Matches the PR description.
- tsc, eslint, guided-contract-editor vitest (6/6), 379 node guards and the added-lines ASCII check pass. No server change in this PR.
- Not verified: saving and reloading the new input, and running a module against it (live preview refuses contract-only drafts, P36).

### P58 - The button to read production days is not shown
The PR text and the locale file offer "Read last 7 days of production", but with Daily averages selected the only button on screen is "Read stored surface cards" (a card action under a daily source). The field list does load automatically, so the flow works, but the button wording and the surface card action under a daily source are confusing.

### P59 - Daily input shows Access = Current
The added daily input carries access "Current" and the dropdown offers only "Current reading" and "History". An author cannot tell the input is the latest finished production day, and there is no way to see or change that. Suggestion: show "Latest finished production day" (a third access choice) for engineering.daily inputs and hide the seconds-based age (see P54).

### P60 - Whole list of 30 fields, no search or grouping
The field list is one long dropdown of provider names with units (MotorAmps, PIP, THT, Vibration, volumes and rates together). Volumes (STB, Mscf) and rates (STB/d) look alike, and fields that do not apply to the asset's lift type are not marked. Suggestion: group by type (volumes, rates, pressures, electrical), add type-ahead, and note when a field is only on some lift types (ties to P53).

### P61 - New input is not selected after adding
After Use as input, the editor still shows the previously selected input (AveragePumpFillage) and the new CHP row is only a plain label at the bottom of the list. The author has to click it to fill in anything else (see P56 for the unit). Suggestion: select the new input and scroll to it.

### P62 - Quantity names are provider-specific
The added input is named after the provider's field (CHP, TotalFluidRateInferred), not a module-friendly name, and the lookup is bound to one provider's vocabulary (ties to P57). Suggestion: let the author name the input separately from the provider field.

## CL 1108783 (#654, e2e: guided fillage case through an imported ForeSite well) - verification and SME review

- Test-only change (one file in AssetApiTests). The project builds (0 warnings, 0 errors) and the added lines are ASCII.
- Not run: the e2e harness needs WSA_ASSET_API_URL, WSA_DATABASE_CONNECTION_STRING and a running asset API, and exits with "WSA_ASSET_API_URL is required" otherwise. It is built for CI, not for the local Aspire stack. No UI to check.

### P63 - The e2e harness cannot run against the local stack
The cross-process provider-first test is the only automated proof that a keyed module resolves bindings from the asset's own connection, but it needs hand-set environment variables and a separate asset API. Suggestion: add a script or Aspire profile that runs it against the local dev stack with the two ForeSite wells, so it can be exercised on this box as well as in CI.

## CL 1108784 (#655, provider required, canonical and immutable on guided drafts - PR 1) - live verification and SME review

Verified on the rebuilt stack (the new migration ran on start):
- The migration cleared all pre-existing drafts: the draft list was empty after start, so every earlier "GAP ..." test draft on the local DB is gone (expected, as the PR states; recreate them when needed).
- GET providers lists ForeSite (full) and CygNet (currentValuesOnly: true).
- Create with no provider: 400 GUIDED_DRAFT_PROVIDER_REQUIRED, "Choose the provider this module is written for (ForeSite or CygNet)."
- Create or edit with an unknown provider (petrovisor): 400 GUIDED_DRAFT_PROVIDER_UNKNOWN.
- A draft saved from the UI starter is stored as "foresite". Editing it to "CygNet" is refused with GUIDED_DRAFT_PROVIDER_IMMUTABLE ("Copy the contract into a new draft..."). Editing with "ForeSite" (mixed case) is accepted and kept canonical. Removing the provider on edit is refused with PROVIDER_REQUIRED.
- Server, Worker, AssetApiTests builds and V2 tests pass. Added lines are ASCII. UI Save draft from the starter works (interim provider).
- Skipped when mirroring (P4 copies diverged): tools/ci/check_api_authorization.py, tools/ci/verify_grant_administration.py, tests/ci/test_check_api_authorization.py (the endpoint inventory count of 115 for the new providers endpoint). Those CI scripts will be one endpoint behind until their own sync.

### P64 - Migration deletes every draft and submission (intended, closed)
Owner confirmed this is deliberate: the core authoring model changed to provider-first, so pre-provider drafts and submissions are not carried over. No action. Only note for release notes: anyone with saved drafts loses them on upgrade.

### P65 - New module has no provider choice yet
The starter silently assigns a provider (foresite for fillage, cygnet for pressure) and the author is never shown it, yet it can never be changed afterwards. Until the chooser (#656) lands, an author cannot know which provider their draft is locked to. Suggestion: show the locked provider as a read-only label on the draft.

### P66 - Provider errors only give a code in the manifest field
GUIDED_DRAFT_PROVIDER_IMMUTABLE has a good message, but the 400s carry no field pointer, so a form cannot highlight the provider control. Suggestion: include the field name in the problem details.

### P67 - The pressure e2e case was dropped
The PR removes the guided pressure e2e until a simulated CygNet exists (#657), so for now pressure is covered by no cross-process test.

## CL 1108787 (#656, provider chooser and provider-filtered pickers - PR 2) - live verification and SME review

Verified in visible Edge (web changes are served live; no restart needed):
- "New module" opens a panel with "Provider for the new module" (Choose a provider first / ForeSite / CygNet) and three starters.
- ForeSite: New threshold module and New contract-only module are enabled; New pressure differential module is disabled.
- CygNet: shows the warning "Current values only. History is not available for this provider yet."; New threshold module is disabled; contract-only and pressure differential are enabled.
- Choosing ForeSite then New contract-only module opens an untitled contract-only draft showing "Provider: ForeSite".
- Opening a saved draft shows "Provider: ForeSite" in the header.
- tsc, eslint, guided-authoring (13/13) and guided-contract-editor (7/7) vitest, 379 node guards pass. One added line has a middle dot (U+00B7) in a file that is already utf8 and already uses it; no other non-ASCII.
- Not verified: the provider-filtered quantity and source pickers on a CygNet contract draft (no CygNet assets with current data were exercised in this pass), and a CygNet pressure draft end to end.

### P68 - Disabled starters give no reason
The starters that do not suit the chosen provider are just greyed out. A user cannot tell why "New threshold module" is off for CygNet or why "New pressure differential module" is off for ForeSite. Suggestion: a one-line reason under each disabled button (e.g. "Needs pump fillage, which only ForeSite serves").

### P69 - New module panel pushes the page around
The panel stacks the provider select and three full-width buttons above "Open draft", moving the draft picker and the Save/Submit buttons down about 200px, and the select changes width between provider choices. Suggestion: a compact row (provider chip + starter menu), or a modal; keep "Open draft" in place.

### P70 - Provider is set before the author knows what they are building
The author must pick a provider before choosing what to build, and a provider can never be changed later (#655). A wrong pick means copying the whole contract into a new draft (#659 adds that). Suggestion: start from the use case (e.g. "Rod buckling", "Idle time") and propose the provider the use case needs, with the reason; let the author override.

### P71 - Draft list mixes providers
The Open draft list shows every draft regardless of provider with no label (the list filter arrives later in #658). Mild, noted to confirm there.

## CL 1108788 (#657, CygNet stand-in for current values)

Six files mirrored (five CLEAN, one new). Gateway, simulator and AssetApiTests build with 0 warnings and 0 errors; ASCII clean. The skipped file is .github/workflows/ci.yml (P4 copy diverged).
Not run live: the stand-in only activates when CygNet:Simulator:BaseUrl is set and needs the provider simulator; the e2e needs WSA_ASSET_API_URL and WSA_DATABASE_CONNECTION_STRING (same limit as P63). The local stack reads the real CygNet, so this changes nothing there.

### P72 - Simulator-only CygNet path cannot be exercised on this box
The stand-in is useful in CI, but the local stack uses live CygNet. Suggestion: document how to point a dev stack at the simulator (one setting and the simulator start command) so the bad-quality rejection case can be reproduced locally without live data.

## Rod Buckling check on usdcforsqlts004 (well "AN   13-14", WellPilotRPOC)

- usdcforsqlts004:8000 is the ForeSite web UI only (static files). The API is on port 80 of the same host (apiUrl in env.js), cookie session from TokenService/CreateToken, same as usdcfstappts010.
- The unfiltered Wells/Well/Filter returns 53,526 wells; with welDeviceTypeValues=WellPilotRPOC it returns exactly one: Name "AN   13-14" (three spaces), Id 891755170315, AssetId 1, FacilityId RPOC_00120, CygNet domain 31313 / site FSSUPPCN / service UIS, PrincipalDeviceType null.
- AllSavedSetPointParams(WellId=891755170315, scanOption=0) lists 20 data groups (DynaCard Config I, Control Parameters I/II, Load Parameters I/II, Fluid Stroke Setup, VSD Control Setup I/II, Speed Trim Adjustment, Crank Switch Position, Analog Input 1-8 ...). Every group came back with SetPointsList null and ErrorMessage "Invalid transaction for data group <type>, ordinal ." (e.g. VSDCtrlS, SpdTrmAdj), so no saved values exist for this well.
- None of the Rod Buckling setpoints (Sec*ScaSpd, Sec*StkLoc, Sec*WoSpLi, Sec*SpdChg, VFDMinSPM, VFDMaxSPM, IdealPF, PODeadBand) could be read. The Lufkin 2.0 groups (GRP_VSD_SPEED_CFG_1/2, GRP_VSD_CONTROL_PARAMS) are not among this device type's groups at all.

### P73 - Rod Buckling cannot run on WellPilotRPOC
The device type has VSD groups but not the Lufkin SAM speed-config groups the script reads. Rod Buckling needs LufkinSam wells with saved setpoints; module authoring gives no way to tell which device types support a use case. Suggestion: a use case declares the device types and setpoint groups it needs, and the picker only offers wells that have them.

### P74 - Setpoint groups with no saved transaction look like errors
A group with nothing saved is returned as ErrorMessage "Invalid transaction ...", not as an empty or not-saved state. Suggestion: the adapter maps this to a distinct "not saved" result so a module can say "no saved setpoints" rather than failing.

### P75 - Well names are not normalised
"AN   13-14" has three spaces, so a search for "AN 13-14" finds nothing. Suggestion: asset search should collapse whitespace.

## usdcfstappts012 check

- Web UI on port 8000 returns 503; the API on port 80 is up (token OK).
- Wells/Well/Filter by device type: LufkinSam 50 (all named SAM_AIRBALANCE_PU_Lufkin-000NN, ids 1341+), WFordKS 31 (GASINJWELL_ReservoirPressure-*), AEBEAM 50 (AEPOC_ROTAFLEX_PU_WFT-*), WellPilotRPOC 0, WFTA1K 0. Counts of exactly 50 suggest a cap or a generated set.
- AllSavedSetPointParams(scanOption=0) for LufkinSam wells 1341 and 1342 timed out after 120 s (no data, no error text). No Rod Buckling setpoints could be read.

### P76 - Setpoint read on this server hangs rather than failing
Saved-setpoint reads time out at 120 s with no error, so a module that needs them would hang a run. Suggestion: the adapter applies its own short timeout and reports a distinct "setpoints unavailable" result.

### P77 - Server looks like generated test data
Well names (SAM_AIRBALANCE_PU_Lufkin-000NN, AEPOC_ROTAFLEX_PU_WFT-000NN) and the 50-well counts suggest a simulated set; the saved setpoints probably have no backing device. Needs confirming with the server owner before using it for Rod Buckling.

## usdcfstappts010 / 011 check (Rod Buckling setpoints)

- 011: UI on 8000 is up, but the API on port 80 resets every connection (token and filter), so nothing could be read. Port 8000 returns the SPA page for any path.
- 010: API works (one transient connection reset at first). Wells/Well/Filter with welDeviceTypeValues=LufkinSam returned 302 wells, but the first ones are ESP, gas lift and test wells (ESP_0473, GASINJWELL-0001, GL-1136, GC_NH_01 ...), so the device-type value on this host appears not to narrow the list to Lufkin SAM wells (or PrincipalDeviceType is unset on most).
- First 25 of the 302 scanned with AllSavedSetPointParams(scanOption=0): only 4 wells had any saved values. Wells 18_5 (id 973), GASINJWELL-0001 (421) and GL-1136-2 (836) each have one group "Basic SetPoints" holding PODeadBand, VFDMaxSPM, VFDMinSPM, IdealPF (e.g. 18_5: deadband 1 %, VFD min 30, max 60 (original 35), ideal fillage 86.55 %). None of the 25 had Sec*ScaSpd, Sec*StkLoc, Sec*WoSpLi or Sec*SpdChg, and none had the GRP_VSD_SPEED_CFG_1/2 groups. espnewdrill (1074) has a "POC set Point" group.

### P78 - Setpoint values come back as strings with a separate original value
Each setpoint carries Value and OriginalValue as strings, with units, range, Enum list and IsHidden. VFDMaxSPM shows Value 60 vs OriginalValue 35. A module needs to know which one is "current" and that numbers arrive as text. Suggestion: authoring declares the setpoint input type (number, enum) and which value it reads, and shows the current-vs-original difference.

### P79 - Group names differ from the original script
The script reads GRP_VSD_SPEED_CFG_1/2 and GRP_VSD_CONTROL_PARAMS; this system exposes the same elements under display groups such as "Basic SetPoints". An authored module must bind to the element (ElementId) not the group, or it will not port between systems.

### P80 - Lufkin 2.0 speed-config setpoints are not on any scanned well
Sec4ScaSpd, Sec4StkLoc, Sec1-3WoSpLi etc. were not found, so the audit and speed-write parts of Rod Buckling cannot be tested on these systems yet. Only the control parameters (VFDMin/Max, IdealPF, PODeadBand) exist, on 3 wells.

## usdcfstappts010 - dashboard-guided Lufkin / RPOC scan

- The Production Dashboard (/fs/dashboards/pedashboard) is not a Lufkin list: it loads all 1,403 wells (Wells/Well/Filter with {}) and offers a Device Type filter (AEBEAM, LufkinSam, SAMIWC, WellPilotRPOC, WFord...). PrincipalDeviceType is null on every well, so the device type cannot be read off a well record, only used as a filter.
- Filter results on this host: WellPilotRPOC 344 wells (AR-RRL2, 32G 17-14P, BG 41-2xH ...), SAMIWC 5 (WAG wells). LufkinSam gave 302 wells once, then reset the connection on three retries.
- Scanned all 344 RPOC wells with AllSavedSetPointParams(scanOption=0), 8 in parallel: 326 failed (connection resets or 30 s timeouts under load) and 18 answered; 8 had saved values: 32G 17-14P (70), BG 41-29H (1635), AR-RRL2 (1468), CB_Test (796), BG 41-28H (1632), DC_RRL_01 (1416), ESP_Test1 (492), Cyclic steam test well (1580). Each carries the four control setpoints (VFDMinSPM, VFDMaxSPM, IdealPF, PODeadBand) in "Basic SetPoints Configuration" (three also in "Control Parameters"). None has Sec*ScaSpd, Sec*StkLoc, Sec*WoSpLi or Sec*SpdChg.

### P81 - Device type is a filter, not a property
A module cannot ask "what device is this well"; PrincipalDeviceType is empty. An authored Rod Buckling module has no reliable way to restrict itself to Lufkin SAM wells; it can only name the setpoints it needs and fail when they are absent. Suggestion: asset applicability should be driven by "has these setpoints saved", checked at preview.

### P82 - Setpoint reads do not survive modest concurrency
8 parallel reads failed 326 of 344 times. A fleet-wide module (all Rod Buckling wells) would need the adapter to throttle, retry and report per-well "unavailable" without failing the run.

### P83 - Same elements, different group names across device families
"Basic SetPoints" (SAM-style wells on 010) vs "Basic SetPoints Configuration" / "Control Parameters" (RPOC wells). Reinforces P79: bind to ElementId.

## WellPilot (RPOC) logic module check on usdcfstappts010 (wells 776 and 457)

Which reference LM fits WellPilot: the controller exposes the setpoints Idle Time (IdleTime), VFDMinSPM, VFDMaxSPM, IdealPF and MinPFA, so the candidates are Idle Time Adjustment and the SPM / target fillage optimization ("RRL_SPM Optimization Logic (Well Pilot)"). Not the Rod Buckling one.

Live saved setpoints (AllSavedSetPointParams, scanOption 0):
- Well 776 (RPOC_00016): "Basic SetPoints Configuration" (13 elements, writable) and "Control Parameters" (28). IdleTime = "00:01:00" with unit s (original 60); VFDMinSPM 0; VFDMaxSPM 10; IdealPF 0 %; MinPFA 0 %; PODeadBand 8 %. In Control Parameters ITFPO = "00:02:00" s (original 60) and IdTmFPO "00:02:00" (original "00:01:00").
- Well 457: IdleTime "00:16:00", VFDMinSPM 4, VFDMaxSPM 10, IdealPF 5 %, MinPFA 4 %, POLoadSP 78 % (original 30), HiLim 24112 (original 22000).
- Well 487: no saved values (all groups empty).

### P84 - The same setpoint appears twice and disagrees
On well 776 the idle time is IdleTime (Basic, 00:01:00) and ITFPO / IdTmFPO (Control Parameters, 00:02:00). A module that reads "idle time" by name could pick the wrong one. Authoring must let the author choose the exact element and show both values; the preview should flag a duplicate meaning.

### P85 - Time-valued setpoints arrive as text with unit "s"
Value "00:01:00" with UnitKey s and OriginalValue "60". A numeric input has to parse hh:mm:ss, while the original value is plain seconds. Matches the blueprint's note; the quantity input needs a "duration text" type or the adapter must normalise.

### P86 - Wells with identical VFD limits make target-fillage logic meaningless
Well 776 has VFDMinSPM 0, VFDMaxSPM 10 and IdealPF 0, i.e. unconfigured. Applicability needs a "configured" check (e.g. IdealPF > 0 and Max > Min) so the module skips it with a reason instead of producing a recommendation.

## Capability check: inputs, outputs and custom logic (build through #656, live in Edge)

Method: ForeSite contract-only draft and threshold draft in the live UI; server draft model read (GuidedModuleDraftContent = manifest + optional ThresholdDefinition).

| Need | What the build does today | Verdict |
|---|---|---|
| Multiple inputs | "Add input" repeats freely (input-1, input-2 ...). Each has a quantity (registered or typed), unit, value type Number/Text, access and age (daily-field inputs add max age). Provider-discovered quantities can be picked (#651/#652). | Works for scalar current and daily values |
| Setpoint, enum, window, anchored, alarm, attribute, lookup, previous inputs | Not offered. Only scalar quantities, daily fields and one structured input (SurfaceCards). No way to read a saved setpoint such as IdleTime or VFDMaxSPM, even though the ForeSite adapter can read them. | Missing |
| Multiple outputs | "Add output" repeats freely (quantity, unit, Number/Text). An output is a named typed value only: no set-point target, no limits, no alarm or recommendation semantics, no write action. | Declares shape only |
| Custom logic | Contract-only drafts say "Logic, sample cases and submission are not available yet." The only runnable drafts are two fixed templates: threshold (fillage below a number) and pressure differential. The IDE tab is an outline of the same form, not a code or expression editor. | Missing |
| Test | Sample cases exist for the two templates only (name, reading, expected outcome); live preview refuses contract-only drafts. | Templates only |

### P87 - Contract and logic are separate, and only the contract can have many inputs
A module with several inputs and outputs cannot have logic; a module with logic has one reading. Idle Time (4 anchored inputs, 1 setpoint, 1 output) and Target Fillage (setpoints + windows, 3 outputs) cannot be authored. Smallest step that unblocks testing: a rule table over the declared inputs that returns the declared outputs.

### P88 - Outputs are values, not proposals
No way to say "propose IdleTime = clamp(...)" with limits, a reason, or to raise an alarm. Add an output kind (setpoint proposal / recommendation / alarm) with target element, unit, min, max, max step and a reason template.

### P89 - Setpoints cannot be inputs
Live data exists (IdleTime, VFDMinSPM, VFDMaxSPM, IdealPF, MinPFA on WellPilot 776 and 457; control values on 8 RPOC wells) and the adapter reads it, but authoring has no input kind for it. Highest-value next input kind for WellPilot, Rod Buckling and Target Fillage alike.

### P90 - No code or expression path for power users
Nothing between "fixed template" and "full code module". Even a minimal expression field (arithmetic, comparisons, clamp, min/max, if) per output, evaluated by the existing pure evaluator, would let Idle Time and Target Fillage run end to end.

## CL 1108789 (#658, provider filter and label on the draft list - PR 3)

Mirrored 10 files (9 CLEAN, Program.cs MERGED). Skipped: tools/ci/verify_grant_administration.py (P4 copy diverged). Rebuilt and restarted the stack (stack14, health 200). Server and worker build, ExecutionContractV2 tests, eslint, guided-authoring (13/13) and guided-contract-editor (8/8) vitest, 379 node guards and tsc pass. Added lines contain only middle dots; guided-contract-editor.test.tsx was typed text, reopened as utf8.

Live results:
- GET guided-module-drafts?adapterKey=cygnet returns only the CygNet draft, ?adapterKey=foresite only the ForeSite draft, ?adapterKey=nope gives 400.
- The Open draft picker labels each draft "name - provider" (GAP list filter 658 cygnet - CygNet, GAP provider 655 renamed - ForeSite); the new "Show drafts for provider" select (All providers / ForeSite / CygNet) narrows the picker to the matching drafts and back.
- A saved CygNet pressure draft shows "Provider: CygNet - Current values only. History is not available for this provider yet." in the header.

### P91 - Changing the provider filter closes the open draft
With a CygNet draft open, switching the filter to ForeSite returned the editor to "Untitled module". Unsaved edits would be lost with no warning. Suggestion: keep the open draft, or ask before closing.

### P92 - Filter and picker label share one line of text
The provider is appended to the name with a middle dot, so a draft named "x - CygNet" is ambiguous, and the filter only affects the picker, not the Reviews tab. Minor; suggest a separate badge and a filter on Reviews too.

### P93 - Filter value is not remembered
The filter resets to All providers on reload. An author who works on one provider has to re-pick it each session. Suggest persisting it per user.

## CL #659 - copy contract to other provider (live)

- **P94** Copy is offered only on contract-only drafts; threshold and pressure-differential drafts cannot be moved to another provider (observed: no Copy control on a ForeSite threshold draft). Suggest explaining why, or copying the contract part.
- **P95** Provider list offers only the other providers (CygNet only here); verified copy produced 'GAP copy 659 (copy)' as a CygNet draft. Copying silently drops source hints and sample cases (per PR text) - suggest a visible notice after copy.


## CL #660 - stored cards report Uncertain

- **P96** Not verified live: the adapter tests skip the live check (FORESITE_BASE_URL unset), so Uncertain quality on real stored cards is unconfirmed. Needs a run against a ForeSite with a stored card library.


## CL #661 - any discovered daily quantity in the production-day preview

- **P97** Live preview not confirmed: POST production-days-preview with a seeded asset (18_5) returned 503 'The integration gateway could not complete the request.' for no quantity, an invalid quantity and a real-looking one alike, so the new quantity projection was not exercised. A random asset id gives 500 UNEXPECTED_ERROR rather than a 404. Suggest a 404/422 for unknown assets and a distinct error for an unknown quantity.
- **P98** Build, adapter checks, V2 contract tests, tsc, eslint, vitest 13/13 and 379 guards pass; the generated client must be regenerated (stack restart) before tsc passes, since the request gains an optional quantity field.


## CL #663 - one source model for module authoring (design doc)

- **P99** The proposal covers inputs only (Current, History, Daily, Latest test, Cards, Setpoint). It does not address multiple outputs with setpoint targets/limits/reasons or custom logic (P87-P90), so the capabilities the reference LMs need (multi-input, proposal outputs, rules) have no design home yet. Suggest an 'Outputs and logic' companion section.
- **P100** The doc lists setpoints as 'read design pending F3'. Live data (P78-P86) already shows setpoint values arrive as strings, group names differ per device type and time values are text; fold these into the Setpoint role definition.


## CL #664 - wrong unit reported separately from wrong quantity

- **P101** Verified by unit tests only (V2 and manifest checks pass). Not seen end to end: a run needs an input whose declared unit differs from ForeSite's (e.g. a time setpoint declared in s vs provider text). The new message in words.ts is shown to authors; confirm it names the input and both units, since 'an input's unit differs' alone does not say which input or what the provider unit is.

