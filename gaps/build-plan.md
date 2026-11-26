# Module authoring build: environment, process and tracking

This branch (`gaps/module-authoring`) is never merged. It is the tracking record for the module authoring build.
The code work happens on `feature/module-authoring-build`, branched from `develop` at #681.

## Process

1. Build step by step on `feature/module-authoring-build`, aiming at one complex module that ForeSite can actually run (Idle Time, Target Fillage or Rod Buckling; picked per step by what ForeSite data we can reach).
2. Test locally first, against a fresh database and the live ForeSite server.
3. Then get the GitHub CI pipeline green.
4. When the feature is done, mirror it back into Perforce one CL at a time, as in the regular process.
5. Gaps and Sri's observations are logged here: gaps in `complex-module-poc-findings.md` (P-numbers), observations in `observations.md` (O-numbers).

## Local environment

- Worktree: `D:\WellSiteAutoPilot\module-authoring`, branch `feature/module-authoring-build`.
- Stack: `dev\WellSiteAutoPilot.AppHost` with the gitignored `appsettings.Local.json` (Negotiate auth, own JetStream folder).
- Database: `wsa_modauth`, created fresh. It is separate from the Perforce stack's `wsa_dev`, which is untouched.
- Web: `http://localhost:5080`.
- ForeSite connection `ForeSite usdcfstappts010` (`http://usdcfstappts010:80/`): test result Healthy, acting as the signed-in Windows user.
- The Perforce-side Trunk now matches `develop` through #681 (CLs submitted apart from the last few pending).

## Where the build stands (develop at #681)

| Capability | State |
|---|---|
| Inputs: current values, daily averages | Works in the workbench (live-checked up to #669) |
| Settings, calculations, ordered rules, setpoint recommendations | Added in #670-#675; not live-tested |
| Rules preview and Tests view | Added in #676-#677; not live-tested |
| Submit and review of rules modules | #679-#681; not live-tested |
| History, setpoint and card sources; multiple outputs; richer functions | Missing (P106, P107, P109-P113) |

## Next steps

1. Live-test the existing workbench end to end on the fresh database, and log what breaks.
2. Pick the first complex module and map each of its inputs to something ForeSite can supply.
3. Fix gaps in order of what blocks that module, one small PR at a time.

## Decisions

- The old module authoring (the guided contract editor and its starters) is removed once the new workbench is stable and the feature is done. Until then both exist; the workbench opens only rules drafts (#678).
- Live check on the fresh database: `#authoring/new` creates a module and opens the workbench with Inputs, Settings, Calculations, Rules and Tests parts plus Save and Submit for review. There is no Outputs part (P107, P112).

## Architect assessment of the design docs (before any code)

Read: foresite-controller-acquisition, foresite-measurement-evidence-plan, foresite-native-applicability, module-authoring-source-model, provider-neutral-authoring-alignment, adapter-operations-framework-plan, adapter-capability-workflows-and-authoring-plan, module-authoring-data-flow-ux, module-authoring-runtime-trace, foresite-adapter, lm-blueprints (Target Fillage).

**Correction to P114.** The two-reading limit is a deliberate, owner-approved evidence gate, not an oversight. Each quantity must have an explicit mapping (field, unit, valid range, time source) plus recorded fixtures before it is offered. "Widen the allowlist" is therefore wrong. The right move is a per-field catalogue where every entry carries its own contract.

**Constraints that shape the build**
1. Time source differs by field family. Controller values (PumpFillage, SPM) use LastGoodScanTime (owner clarification, 27 Oct 2026). Daily fields (CyclesYesterday, RunTimeYesterday, IdleTimeYesterday, InferredProductionYesterday) and well-test fields (LatestValidWellTestDate) must not borrow it; each needs its own time rule.
2. Fail closed: missing stays missing (never zero), unknown quality is rejected, applicability must come from the provider, not from populated fields. WellQuantity enum IDs are unverified and must not be used as keys.
3. Modules declare what they need (role, capability, typed ports), never how to call ForeSite. Adapters own operations; the catalogue is the single source (one registry, no second one). Capability availability is per connection revision and asset, never "Available" from the declaration alone.
4. One source model for authors: Current value, History, Daily value, Latest test, Cards, Setpoint. No operation names in the UI. One verdict per input shared by preview and compatibility.
5. Authored draft schema is single and versioned. Test cases are first-class fixtures. Traces are module-reported evidence only.
6. Safety: shadow only, zero control commands, no provider writes.

**Target Fillage needs (blueprint) vs what exists**
- Inputs: setpoint roles (MinSpeed, MaxSpeed, TargetFillage, MinFillage), window inputs (AvgFillage, AvgSpm over N hours), current CyclesYest and RunTimeYest, attributes.
- Logic: ordered Gate rules, then an Action table returning three values (NewMin, NewMax, NewMinFillage) with reasons.
- Proposal: setpoint group, only changed members, group invariants, alarm output.
- Built: rules engine (single-valued), settings, calculations, current-value inputs. Missing: window inputs, setpoint inputs, multi-value rule outcomes, group invariants, alarm output (P110, P111, P112, P113).

**Build order (each a small PR, tested locally then CI)**
1. Provider catalogue: replace the two-name allowlist with a per-field catalogue for ForeSite surveillance and daily fields, each with unit, range, time source and fixtures; discovery returns the full list with latest value, time and quality. (P114, P106, P12)
2. Daily and window inputs usable in rules modules (P113); submit with the real data requirements.
3. Setpoint inputs by role (read design F3), then multi-value rule outcomes (P111) and outputs part in the outline (P112).
4. Invariants and alarm output; then model Target Fillage end to end and prove it on the two wells.
5. Only after the new workbench is stable, remove the old authoring.

**Open questions for Sri (need domain evidence, not guesses)**
- Units and valid range per surveillance field (the payload has none). Is there a ForeSite reference (PointFieldList/{wellType}) we can read for them?
- Time rule for the daily fields (production-day end?).
- Which two wells to use.

## ForeSite source findings (from ForeSiteService Trunk, with Sri's clarifications)

- `WellQuantity` (Common/POP.Enums/QuantityEnums.cs) is the generic surveillance catalogue: about 75 members, each with `[ValidForWellTypes]` (applicable well types), `[Unit(UnitKey, category)]`, and an optional `[Description]` label. `DailyAverageQuantity` and `WellTestQuantity` follow the same pattern. The adapter can offer the whole list as a typeahead without knowing the asset or well type.
- The surveillance response (`GET Surveillance/well/{id}`) is `UnitsValuesPairDTO`: `Units` gives per-field `UnitKey`, `Precision`, `AssumeCorrectUnitFromCygNet`; `Value` gives the numbers. There is also `well/{id}/{unitSystem}`. So units are available per response, not guessed, and the adapter's hard-coded "%" was avoidable.
- Value DTOs differ by well type (RRL, ESP, GL, NF, ...), so the field list per well type comes from the enum attributes and the DTO class.
- Time: surveillance values share `LastGoodScanTime` (datagroup scan success); `LastScanTime` is the last scan issued. Well test fields share one test date (`SampleDate` and `TestEndDate` are UTC datetimes at local midnight, e.g. 2024-09-16T05:00Z).

## Daily average time rule (Sri, confirmed)

Daily average values are written to the database at the end of the production day. A record's time is its day: `StartDateTime` to `EndDateTime`, not a scan time. The adapter's daily reader already returns `days[].start/end` with offsets and selects whole days by StartDateTime in [start, end). Like surveillance, the response has its own `Units` block (UnitKey, Precision). Consequences for the catalogue: each entry carries its time source (surveillance = LastGoodScanTime, daily = day start/end, well test = test date), and a module that reads "yesterday" resolves to the latest closed production day (P113), not to a surveillance Yesterday field.

## Roadmap: module applicability constraints (after the first end-to-end module)

Decided with Sri: do not build now; revisit once a logic module is built end to end from the UI.
- A module carries asset-attribute conditions, separate from its data inputs and independent of live assets.
- Rule shape: attribute, operator ("is any of" / "is none of"), values. Conditions AND across attributes, values OR within a list. Matching is trimmed and case-insensitive.
- Attributes come from the ForeSite WellFilterDTO facets: well type, device type, SCADA type, composite well subtype, wellbore profile, field, region, lease, UserDef01-20. Status, down time, foreman, engineer and gauger beat are excluded; logic mode and assigned modules are later.
- Well type is a fixed set (WellTypeId) shown as an Applies to / Excludes mode with multi-select chips. Default Any. Other attributes are free text with a typeahead from imported assets and a warning when no asset has the value.
- Evaluated first when an asset is selected; input checks run only if applicability passes. Missing attribute fails closed ("can't tell"), pending Sri's confirmation for "none of".
- Not in scope: wildcards, ranges, OR across attributes, numeric comparisons.
- Prerequisite check: which attributes the import keeps from discovery.

## Decisions confirmed by Sri
- Surveillance Yesterday/Today fields (CyclesYesterday, RunTimeYesterday and similar) stay in the Current values list, stamped with LastGoodScanTime.
- Applicability constraints are deferred to the roadmap (see above).
- Units (Sri): modules always read in one fixed unit system, ForeSite default (assumed imperial), as a requirement. Finding: in ForeSite, `UnitSystemEnum.Default` means the unit system the calling user has configured, so `well/{id}` returns the gateway account's own setting, which is not guaranteed to be imperial. Requirement for the adapter: the catalogue holds the expected UnitKey per quantity; each read compares the response `Units` UnitKey to it and fails closed on a mismatch (or requests an explicit unit system). Verify what the gateway account returns on the live server.
- Adapter requirement (Sri): the Integration Gateway's Windows account must have imperial set as its ForeSite unit system. The adapter still compares each response UnitKey with the catalogue and fails closed on a mismatch; the connection test and install docs should state the requirement.
- Well test date: the ForeSite WellTestDTO has no field named WellTestDate. Its dates are SampleDate (required, "Sampling date time", and the latest test is chosen by SampleDate descending), TestEndDate (optional), TestStartDate (commented out in the DTO), LastChangedDT (edit time) and SyncDate. Proposal: SampleDate is the applicable date for all well test fields; the surveillance LatestValidWellTestDate is the same test seen from the status side. Pending Sri.
- Well test date confirmed (Sri): SampleDate is the applicable date for all well test fields.

## Decision: authoring lists the catalogue, assignment decides presence
The Inputs list is the provider's full catalogue (asset-independent). Presence and applicability are only knowable once the module is assigned to an asset, so the check belongs to assignment time. Units in the surveillance payload mark a field as defined, not as present for a well (e.g. AveragePumpFillage has unit % but is null on RRL well 1596). Step 2 reports each field as observed, missing or unit-mismatched at assignment, not at authoring.


## Check: external authoring
A guided draft is approved by building a package and calling LogicModulePackageService.UploadAsync, then ApprovalService.ApproveAsync (GuidedModuleDraftService lines 73-74). An externally built .wsamodule goes through POST /logic-modules/packages into the same UploadAsync and the same review. So both paths converge on one registration and approval. The provider catalogue is public via GET /connections/adapter-operations. Rule for the build: the web UI uses only public API and every authoring rule lives on the server. Draft-only today: preview, quantity discovery and submit-with-readings (an external author cannot preview). Overlap note: 15 catalogue names (GasInjectionRate, MotorAmps, PumpSpeed, Vibration and others) also exist as daily-average fields; on the current path they mean the controller scan.


## Step tracker (plan vs state, 19 Nov)

Each step is done only when: designed here, small PR, positive and negative tests, live-checked on the fresh stack, CI green, then mirrored.

| Step | Plan | State |
|---|---|---|
| 1 Provider catalogue | Per-field catalogue for surveillance AND daily fields; unit, range, time source, fixtures; full-list discovery | Surveillance done (43 fields, units checked, fixtures). Daily catalogue done (82 fields, imperial units, unit mismatch refused, count unit), time source stored per catalogue (scan time / production day) - commit df11c258. **Not done:** per-field range for non-percent quantities (no evidence of valid bounds yet; stays finite-number check only), daily catalogue not yet offered through adapter operations/UI. Live: well 776 gave 23 observed, 20 no value, 0 unit mismatches, so the account is imperial |
| 2 Daily and window inputs, real data requirements on submit | Not started. Discovery now one read (done); typeahead, generator scripts in repo outstanding |
| 3 Setpoint roles, multi-value outcomes, Outputs part | Not started |
| 4 Invariants, alarm output, Target Fillage end to end, two wells | Not started |
| 5 Remove old authoring | Not started |

Open before PR 1: draft PR not opened (gh missing); CI not run; web not re-checked on the stack after restart.

## Roadmap: limit ForeSite asset discovery to SCADA-connected wells (Sri, 26 Nov)

The main intent is Logic Modules that adjust setpoints, so wells without a SCADA connection add little value. Discovery should offer only the ForeSite assets that have a SCADA connection, instead of every well. Not prioritised yet; to be discussed first. Open points: how ForeSite exposes the SCADA link (to check in ForeSiteService), whether it is a filter on the scoped discovery call or a separate query, and how import treats wells whose connection is added later.


## Finding: ForeSite user-configurable surveillance fields (UDCs), checked 26 Nov

Source: ForeSiteService Trunk (ConfigurableUDCInfo.cs, SurveillanceService.Net10.cs, WellGroupStatusDTO.cs) and the saved well 1596 payload.

- ForeSite keeps up to 60 optional field slots per well type. Each slot is an installation setting with Name (free text, for example "Daily Volume today Steam"), UDC (the CygNet point suffix, for example VOLSTEAMT), UnitKey, UnitCategory, CygNetPointType, an optional daily-average name (dailyAvgName) and three switches: UseForWellStatus, UseForWellTrend, UseForGroupStatus.
- The status document carries the values as strings OptionalField01..OptionalField60. Slot N is described by OptionalFieldInfo[N]: Name, UDC, Unit and Present. Present says whether this well really has the point, so presence is per well, but the list of configured names and units is per well type and is known without any well.
- The Units block already holds the unit per slot (well 1596: OptionalField01 bbl, 02 bbl, 03 bbl/d; 04..60 null). The configured names for those three slots are "Daily Volume today Steam", "Daily Steam yesterday" and "D steam average rate", all Present=false on that well.
- Discovery routes that return the configured list without a well: POST Surveillance/OptionalQuantities/{wellType} (only Present, analog, UseForWellTrend entries) and GET Surveillance/OptionalPointFieldList/{wellType} (names only). The same slots feed the daily average through OptionalDailyAverageUDCDTO (matched by UDC) when getOptionalUDCs is true.

Consequences for authoring:
1. These are not free text. The author's list should include the configured optional fields (name plus unit) next to the fixed catalogue, per well type, read from ForeSite at authoring time. The fixed catalogue stays generated from source; the optional list is installation data and must be read from the connection.
2. A module reads a slot by its Name, but the slot index can differ per well type and the Name is editable in ForeSite. The reader must resolve Name -> slot through OptionalFieldInfo at read time, check the unit, and fail closed if the name is missing or ambiguous.
3. Values are strings, so parsing must be strict (invariant culture, empty is missing, never zero).
4. A manual entry (name and unit) stays as the exception path. It is accepted only when the read can verify the unit against the live Units block.
5. Open: whether OptionalQuantities needs a well type the author must pick, or can be merged across the well types of the connection.
