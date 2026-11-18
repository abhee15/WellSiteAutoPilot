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
