# Authoring UX and layout

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P3 | UI for a contract-only draft is view-only: no Save, Submit, Run sample cases or Run live preview buttons (c... | Open | No | Gaps |
| P4 | No UI path to create a contract-only draft (API only). Not verified beyond the "New module" button; the cli... | Open | No | Gaps |
| P6 | No delete endpoint for drafts; test drafts persist in local DB (~3 contract-only + "POC complex renamed" x2... | Open | No | Gaps |
| P9 | the 400 above surfaces only as "The operation could not be completed. Check access or reopen the latest dra... | Open | No | Live verification of #636 (CL 1108673), Edge visible, 2026-11-17 |
| P11 | a new input starts with the default quantity "Quantity1", which can never be saved, so every new input star... | Open | No | Live verification of #637 (CL 1108674), Edge visible, 2026-11-17 |
| P13 | the output port also defaults to the unsaveable name Quantity1 (same as P11). | Open | No | Live verification of #638 (CL 1108675) |
| P14 | the IDE tab button does not respond to a role=button click in automation (found by text only); check its ac... | Open | No | Live verification of #638 (CL 1108675) |
| P23 | the draft picker now lists 30+ drafts with duplicate names (several "CL638 sample check", "Live check press... | Open | No | Live verification of #640 (CL 1108677) |
| P29 | Well-test panel shows raw values: ISO timestamps with +00:00 offsets, a bare "Test status 2103" code, and e... | Open | No | CL 1108757 (#646, empty well test = no test; contract panel direction/ |
| P35 | Live preview panel layout, button sizing and data presentation (user-reported, screenshot) | Open | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P39 | Developer caveats shown to authors | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P41 | Surface card result is hard to interpret | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P42 | Placeholder text leaks into asset labels | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P43 | Run live preview and source pickers stay enabled for an asset that cannot be checked | Open | No | CL 1108759 (#648: history preview for numeric inputs, no-source messag |
| P45 | History button label and placement | Open | No | CL 1108759 (#648: history preview for numeric inputs, no-source messag |
| P49 | Layout of the source fields | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P58 | The button to read production days is not shown | Open | No | CL 1108782 (#652, add a discovered production-day field as a daily inp |
| P59 | Daily input shows Access = Current | Open | No | CL 1108782 (#652, add a discovered production-day field as a daily inp |
| P60 | Whole list of 30 fields, no search or grouping | Open | No | CL 1108782 (#652, add a discovered production-day field as a daily inp |
| P61 | New input is not selected after adding | Fixed on develop | No | CL 1108782 (#652, add a discovered production-day field as a daily inp |
| P68 | Disabled starters give no reason | Open | No | CL 1108787 (#656, provider chooser and provider-filtered pickers - PR  |
| P69 | New module panel pushes the page around | Open | No | CL 1108787 (#656, provider chooser and provider-filtered pickers - PR  |
| P70 | Provider is set before the author knows what they are building | Open | No | CL 1108787 (#656, provider chooser and provider-filtered pickers - PR  |
| P71 | Draft list mixes providers | Open | No | CL 1108787 (#656, provider chooser and provider-filtered pickers - PR  |
| P91 | Changing the provider filter closes the open draft | Depends on whether the draft filter/copy survives the redesign | No | CL 1108789 (#658, provider filter and label on the draft list - PR 3) |
| P92 | Filter and picker label share one line of text | Depends on whether the draft filter/copy survives the redesign | No | CL 1108789 (#658, provider filter and label on the draft list - PR 3) |
| P93 | Filter value is not remembered | Depends on whether the draft filter/copy survives the redesign | No | CL 1108789 (#658, provider filter and label on the draft list - PR 3) |
| P94 | Copy is offered only on contract-only drafts; threshold and pressure-differential drafts cannot be moved to... | Depends on whether the draft filter/copy survives the redesign | No | CL #659 - copy contract to other provider (live) |
| P95 | Provider list offers only the other providers (CygNet only here); verified copy produced 'GAP copy 659 (cop... | Depends on whether the draft filter/copy survives the redesign | No | CL #659 - copy contract to other provider (live) |
