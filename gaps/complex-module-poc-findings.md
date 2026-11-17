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
