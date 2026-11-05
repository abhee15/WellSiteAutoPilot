# ForeSite adapter: capability gaps for Logic Modules

Status: working document, updated as reference logics are reviewed. Source of truth for the gap list and the proposed build order; individual gaps become GitHub issues and PRs one at a time.

Reference logics are used only to capture intent and the data they need. Legacy implementation details (persistence, sleep loops, polling) are not requirements.

## Context
The legacy VSD "Target Fillage Phase 1" ACL needs more than the ForeSite adapter exposes today. Reference ACL inputs and outputs are compared with the adapter below. Findings come from `ForeSiteAdapter.cs`, `ForeSiteMeasurements.cs`, `AdapterReadRules.cs`, `ProviderContracts.cs` and the ForeSiteService interfaces (`POP.Interfaces`).

Today the adapter advertises only `AssetDiscovery` and `CurrentData`. Current reads support only `PumpFillage` and `AveragePumpFillage`, each as a `%` value in the range 0-100. The contract already defines `HistoricalData`, `Alarms`, `Command` and `Verification` (`IHistoricalDataProvider`, `IAlarmProvider`, `ICommandProvider`, `IVerificationProvider`), but ForeSite implements none of them.

## Gaps

| # | Gap | Legacy ACL need | ForeSite source already available | Size |
|---|-----|-----------------|-----------------------------------|------|
| G1 | More current quantities | `CurrentSPM`, `CyclesYesterday`, `RunTimeYesterday` | `Surveillance/well/{id}` `Value` object (`RRLWellStatusValueDTO`) already returns them; units are in `RRLWellStatusUnitDTO` | S |
| G2 | Units and ranges are hard-coded | The adapter forces `%` and 0-100 for every quantity | Per-quantity unit and precision come from the Units block | S |
| G3 | No quantity catalog for authors | Authors must guess supported quantities, units and per-well-type availability | `Surveillance/AvailableQuantities/{wellId}`, `WellControllerCapabilities/{wellId}` | M |
| G4 | Setpoints cannot be read | `VFDMinFrq/SloSpeed`, `VFDMaxFrq/PkSpeed`, `IdealPF/PmpFillPer`, `MinPFA` | `WellConfig/GetWellAutonomousControlGroupData` (already called for device types) | M |
| G5 | No history | Averages of SPM and fillage over the last N hours | `Surveillance/GetTrends/{well}/{start}/{end}` returns daily averages only. Hourly history may exist only via CygNet. Needs a decision | M-L |
| G6 | Module parameters come from ForeSite ACL settings | Sheaves, motor RPM, reducer ratio, step limits | `GetACLLogicSettings`, `GetLogicModuleForWellId` | M |
| G7 | Alarms | Set and clear `VSD_Minimum_Fillage` | Read: `CurrentAlarms/{wellId}`. Write: `SetWellAlarms`, `ClearWellAlarms` | M |
| G8 | Recommendations and writes | Recommendation mode and autonomous setpoint writes | `ApplyRecommendationForEachSetpoint`, `Well/Command`, ASU workbench. Needs a governed design (Shadow, then recommend, then approved write) and `Verification` readback | L |
| G9 | One HTTP call per quantity | Each quantity re-reads the whole `Surveillance/well` document | Read once per execution and fan out | S |
| G10 | No ForeSite simulator for CI | Modules cannot be tested without the live ForeSite | Fake ForeSite (or simulator adapter) carrying the new quantities and setpoints | M |

## Additional gaps from logic 2: Rotaflex Target Fillage Phase 2

| # | Gap | Legacy ACL need | Notes | Size |
|---|-----|-----------------|-------|------|
| G11 | Setpoint groups, not single names | Groups `VSDCtrlPar` (`VFDMinSPM`, `VFDMaxSPM`), `VSDSTA` (`STAEnR`, `MxTnSp`, `MxUpSp`, `MxDnSp`, `SpTmFct`, `DnSpDif`, `DnSpDDR`) and `VSDCtrlPar`/`IdealPF` | Extends G4 to read named groups per device type | M |
| G12 | `AverageSPM` quantity | Averages from the CygNet UIS | Same family as G1; may be CygNet-only. The adapter-to-quantity mapping must be explicit per adapter | S |
| G13 | Device type awareness | Branches on `DeviceType.WellPilot` | Discovery already carries `deviceType`; modules need it as an asset attribute or trait | S |
| G14 | Intelligent alarm reads | Count of active intelligent alarms before and after a change | `IntelligentAlarms/ActiveIntelligentAlarms/{wellId}/{wellType}` (extends G7) | M |
| G15 | Reference data (pump speed tables) | Rotaflex speed table: max speed, max cornering speed, lookup by speed | Today a CSV or a service call (`get_rotaflex_speed_data_for_well`). Needs a source: asset parameter, module resource or a service | M |
| G16 | Module state between runs | (Legacy only) "Yesterday's mean SPM and average fillage" from a saved file | **Not a requirement.** Legacy persistence detail. Intent: compare with the previous day's outcome. Decide separately whether the platform needs run history for modules | - |
| G17 | Long-running closed loop | (Legacy only) Sleeps up to 24 h, checks alarms and reverts | **Not a requirement.** Intent: make a small change, watch for ill effects, back out if needed. Decide how to express this in WSA (scheduled runs, verification, revert) | - |
| G18 | Manual-change detection | (Legacy only) Compares setpoints in a loop | **Not a requirement.** Intent: don't fight an operator. Decide how to express this in WSA | - |

**Legacy defects spotted in logic 2 (fix in any port):** the safety-loop's revert uses `val_max_spm` which is inconsistent between branches; `val_max_spm_new` can be unset when the speed record has no match (`speed_rec_match` can be `None`); the save key differs between load (`-`) and save (`_`), so yesterday's data never loads and defaults to 0.0; early `return` without an `AclExecutionResult`; `acl_params.validate()` result ignored.

## Proposed order (one gap per PR)
1. G1 + G2 + G9 (small read-only change; needs a per-quantity table: field, unit, range, well type)
2. G3 (catalog endpoint, also feeds the starter kit download)
3. G10 (simulator support so the next gaps are testable in CI)
4. G4 (read-only setpoints)
5. G5 (decide: daily trends only vs CygNet hourly history; then implement)
6. G6 (parameter pre-fill)
7. G7 (alarms: read first, writes later)
8. G8 (recommendations and writes: design review first)
9. G13, G11, G12, G14, G15 (smaller read-side gaps as the logics need them)
10. G16-G18 are legacy implementation details, not requirements. Capture the intent per logic (see below) and design the WSA approach separately.

## Intent of each reference logic (platform-neutral)
- **Target Fillage Phase 1 (VSD):** keep average fillage near a target by trimming the VFD speed range. If fillage is low and speed is low, lower the minimum speed. If fillage is low and speed is at or above target, lower the maximum speed. At the floor, raise a minimum-fillage condition. Guarded by sanity limits on step size, cycles, run time and min/max span.
- **Rotaflex Target Fillage Phase 2:** when fillage is at or above target and the operating point is stable, raise the maximum speed in small steps, within the pumping unit's limits. Back out if the well shows alarms, and require Speed Trim Adjustment beyond the cornering speed.

## Test plan
- Develop on a feature branch against the local ForeSite (2 wells), in Shadow only.
- After each gap: build and pack the reference module, run Shadow, and check the execution result.
- Do not change the dev stack or shared databases.

## Out of scope for now
- Any provider write (G8) until the design is approved.
- Non-RRL well types (ESP, gas lift, PCP) until the RRL path is proven.

## Related UI/authoring items
- Starter kit download on the Connections page (adapter card) and a link on the Logic > Modules tab, served from one endpoint (`GET /adapters/{key}/starter-kit`).
