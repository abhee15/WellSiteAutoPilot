# Observations from Sri's own testing

Log each observation here as O1, O2 and so on, with the screen or step, what was seen, and what was expected.
Each one is triaged into a gap in `complex-module-poc-findings.md` or fixed on `feature/module-authoring-build`.

| # | Date | Where | Observation | Expected | Status |
|---|---|---|---|---|---|

- **O1** Once the new module authoring page covers the agreed functionality, remove the old authoring components (the legacy threshold editor and its sample cases, live preview wiring and the dropdown-based draft list) so there is one path to maintain. Before removal the new page needs: a drafts list (P116), the review queue and approval, persisted scenarios (P117), and live preview on a real well. Tracked as step 5 of the build plan.

- **O2** Product focus for the ForeSite adapter: logic modules exist to adjust setpoints. Anything beyond that (calculating other values and pushing them back to ForeSite) is a later expansion, considered only if it proves valuable to users. Sri will share customer scripts to inform that future work. Until then every step is judged by whether it serves the setpoint workflow (steps 3 and 4 of the build plan).

- **O3** ForeSite history: a separate 'Well trend data' section is not wanted. Whatever is readable as a current value should also be readable as history over a time range, so history reuses the surveillance field list (today only PumpFillage and SPM are hard-coded in ForeSiteHistoricalDataProvider). Plan: generate history operations from the catalogue, drop the separate group, and verify on live wells which fields CygNet historizes, failing closed for the rest.

- **O4** Range calculations in a module need a good authoring experience. Examples: well downtime over a period, and production over a period summed from daily averages. Today a module reads one current value or one history window and writes its own loop. Plan (after O3 and the setpoint steps): provide built-in range aggregations the author picks rather than codes (sum, average, min, max, count, time-in-state/downtime, last-N-production-days) over a chosen window (last N hours/days, production day, custom), with a scenario preview that shows the window, the rows used and gaps. Downtime needs a state definition (for example SPM below a threshold or a status field) and a rule for missing data. Daily sums must respect production-day boundaries and fail closed on missing days. Keep it tied to the setpoint focus (O2): a setpoint can depend on such a result.

- **O5** Authoring experience needs a senior design and research pass. The workflow mode is being built, and the IDE mode and other modes were discussed earlier. Before more authoring UI is built, produce a UX research and design brief: user types (field engineer, production engineer, power author), tasks (setpoint rules, range calculations from O4, review), mode comparison (workflow, guided form, IDE/code, outside-WSA authoring), where each mode fits, how they share one draft and one package contract, and a recommended primary path with testable usability criteria. Output goes to the build plan; UI work on later steps (drafts list P116, scenarios P117, range calculations O4) follows its recommendation.

- **O6** After scenario tests pass, the user must be able to run the module against a real well configured on that deployment as a preview (read-only, nothing written back). The old authoring page had this live preview (GuidedLivePreviewService and its wiring); the new page must have it before the legacy components are removed (ties to O1). Needs: well picker limited to wells with a ForeSite connection, per-input live read with the capability routing from P113, shown value, unit, timestamp and source, the decision the module would make and the setpoint it would write, and a clear read-only label.

- **O7** Expressions: how does an author know which expressions are allowed and how to write one? Today the rule and calculation boxes only show clickable chips for the names in scope and for the functions (min, max, avg, abs, round, clamp, if) with no syntax help. Expected: an expression helper in the editor with the operators, each function's signature and a one-line example, the type of every name (number, text, true/false) with its unit, autocomplete, live validation that shows the error position and the result type as you type, and a check against the Tests values so the author sees the evaluated result. Include the same reference in the IDE mode and in the published docs. Feeds the O5 design brief and the O4 range functions (sum, count, time-in-state would be added as documented functions).

- **O8** Is the current workflow mode the best experience we can give? Challenge it rather than polish it. Today it is a tabbed form (Inputs, Settings, Calculations, Rules, Tests) where the author writes expressions in text boxes and sees the result only after running scenarios. Questions for the O5 design pass: should the page follow the flow of the logic (read, calculate, decide, set) as one visible canvas or step list instead of tabs; should each step show its live value and result beside it (a worked example column) so there is no separate Run; should rules be written in plain sentences or as a decision table (conditions in columns, outcome in rows) with expressions only as an advanced option; should the author start from templates for common setpoint cases (target fillage, SPM step, min/max clamp); should settings be separated into what the engineer tunes per well and what is fixed; how are mistakes caught (inline errors, unreachable or overlapping rules, missing-data behaviour). Deliverable: two or three prototyped alternatives tested with real users on the SPM optimizer and Target Fillage tasks, compared on time to first working module and error rate, before any more workflow UI is built.

## O9 - Module authoring is a framework feature

Sri: always look at module authoring as a framework feature, not a ForeSite feature. ForeSite is the first adapter; the authoring model, validation, expression helper, history/range operations, live preview and package contract must stay adapter-neutral and be driven by adapter capability catalogues (inputs, settings, history, daily, setpoints). Plan: review every new piece (daily registry, history catalogue, capability routing, expression checker) for ForeSite-specific assumptions; keep ForeSite names in the adapter, not in Application or web; add a second-adapter check (Wellflo or simulator) to prove neutrality.

## O10 - Modules must be buildable outside the application

Sri: make sure modules can be built from outside the application. Plan: the package contract (manifest, rules/code, tests) is the single artifact; the authoring kit/CLI validates, tests and packs it offline using the same checks as the app (including the expression checker, driven by an adapter capability catalogue file); import into any deployment through the existing package import path. Ties to O5 (outside-WSA mode) and O9 (framework feature). Verify with an end-to-end: author in a folder, validate and test with the kit, import, review, publish.


## O11 - Extend the ForeSite-style adapter work to WAMI

Sri: the adapter work could also reach WAMI; the Wellflo repo has the solution that runs Weatherford.WAMI.Server. Plan: (1) research in the Wellflo repo (project memory, WAMI.Server host, its API, auth, data model for wells, current values, history and setpoint writes); (2) map WAMI onto the adapter capability catalogues (inputs, daily, history, setpoints) and list the gaps; (3) stand up WAMI.Server locally on this box and run it against real or simulated wells; (4) build a WAMI adapter POC on a feature branch as the second adapter that proves O9 neutrality (no WAMI names in Application or web); (5) run the same authoring, preview and setpoint flow against it. Sequenced after the current module-authoring CI fix and P118/P121 work. Output: a WAMI findings doc and a build plan before any adapter code.


### O11 research findings

- WellFlo checkout: D:\P4\WFT_Services\WellFlo\WellFlo_R2_Release, solution WellFloAPI.sln (Weatherford.WAMI.Server plus Hosts, Core, Interfaces).
- WAMI is a WCF net.tcp router (port 5656, Windows auth, WCFAllowedGroups) fronting worker processes (max 10, no queue, COM-based WellFlo components). Needs the installed, licensed WellFlo stack, so it cannot run from a plain dev checkout.
- It is a well-analysis/model service (nodal analysis, model data get/set, operating-point overrides for rod lift, ESP, PCP, gas lift, plunger lift). No asset discovery, current values, history or daily values, and its setters change the calculation session, not stored well setpoints.
- Consequence: WAMI is not a second telemetry adapter. It fits as a calculation provider: a module input or calculation backed by a WAMI model run on a ForeSite-sourced model (ForeSite ModelFiles/CurrentWellModel/{wellId}). Existing design: D:\WellSiteAutoPilot\wt-wami-provider\docs\design\wami-provider-and-multi-provider-logic.md.
- New framework gap P127: authoring has no concept of a calculation provider (inputs that are computed by an external engine, with run cost, licence and latency limits). Needs a capability kind alongside current/daily/history. Next: read the wt-wami-provider design, then decide plan; a true second telemetry adapter for O9 should be Wellflo/simulator instead.


## O12 - Module authoring has no way to reach saved drafts on first click

Sri: when I first click Module authoring there is no option to look at saved drafts. Related to P116 (a drafts list with dates and status). Plan: the Module authoring landing shows the author's drafts first (name, adapter, status, last saved, open or delete) with New module beside it, so returning to unfinished work is one click; reachable from the draft page too (All drafts). Verify headed in the browser, then add to the O5 design brief.


## O13 - Check the UI as it is built

Sri: are you checking the UI as it is being built? Practice from here: every web change is followed by a headed Edge run (slowMo) of the real flow against the local stack, reading the page text and a screenshot, and judging the experience (not only that it works) before commit. Findings go into the gap log. Headed runs so far found: drafts hidden behind a dropdown (O12), live section above the scenarios, no-op chips too long, Save enabled by scenario-only edits.


## O14 - Full lifecycle from module authoring

Sri: from module authoring I must be able to create a logic module, send it for review, have it approved, map it to an asset, and test it in shadow, recommendation and autonomous modes. This is the acceptance scenario for the feature; verify each step headed against well 776 (RPOC_00016+9077) and log every break as a gap.


## O15 - Scheduling must work end to end

Sri: after the lifecycle (O14) check that the scheduling piece works: an approved module mapped to an asset actually runs on its schedule in shadow, recommendation and autonomous modes, and the runs appear on the Runs page. Verify against well 776; log gaps.


## O16 - UI refinements

Sri: UI refinements. Treat as a polish pass after the authoring gaps: sentence-case copy, spacing, long chip lists, disabled buttons that say why, scenario table density, consistent landing. Judge each headed run and screenshot.


## O17 - Authoring modes: Workflow and IDE

Sri: the new authoring page must offer the different modes discussed - Workflow mode (guided, step by step) and IDE mode (code-first, expression/manifest editing with the same validation, preview and tests). The legacy page already has a Workflow/IDE switch (readEditorView); the new page has only the workbench. Plan: one draft, two views over the same model, switchable without loss, preference remembered; IDE view backed by the offline kit (O10) so the same module can be built outside the app. Part of the O5/O8 design brief.
