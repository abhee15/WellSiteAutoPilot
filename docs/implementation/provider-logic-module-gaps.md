# Provider adapters: capability gaps for Logic Modules

## Scope: provider-neutral gaps

A logic's intent can be served by either ForeSite or CygNet. A module declares the quantities, setpoints, history and actions it needs. Any provider adapter that satisfies that contract can supply them. So each gap below is a gap in the **provider contract**, and is closed once per adapter:

1. Define the neutral capability in `Integration.Contracts` (quantity catalog, units, history, setpoints, alarms, commands).
2. Implement it in ForeSite and in CygNet, each from its own source.
3. Prove it with a provider-neutral module test, run against each adapter or its simulator.

ForeSite details and sources named in the tables are the first implementation. For each gap, the CygNet source (point or UIS facility) is to be filled in as it is investigated. The "ForeSite source" column is not a statement that the gap is ForeSite-only.


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

## Additional gaps from logic 3: Target Fillage Phase 1 for Unico (SPM)

| # | Gap | Legacy ACL need | Notes | Size |
|---|-----|-----------------|-------|------|
| G19 | Controller-specific setpoint names | Unico names: `UMINSPM`/`DMINSPM`, `UMAXSPM`/`DMAXSPM`, `TPMPFIL`, `MINPFA`, in a Unico control group | Same intent as logic 1 on a different controller (VSD: `VFDMinFrq`, `VFDMaxFrq`, `IdealPF`). Needs a per-device-type setpoint map (extends G4/G11 and G13) rather than names baked into each module | M |
| G20 | Paired setpoints | Up and Down speeds are written together with the same value | A command or recommendation may carry several setpoints that move as one (G8) | S |
| G21 | Recommendation target type | Recommendations are typed by device action (basic, control) | G8 design: the action kind must come from the adapter, not the module | S |

Logic 3 is logic 1 with SPM instead of Hz. Intent, sanity checks and decision tree are the same, and it reads the same quantities: average fillage, average SPM over N hours, cycles yesterday, runtime yesterday. The shared pattern (target, current, step, floor/ceiling, min/max span, sanity gates) suggests one parametrised module with per-controller setpoint maps rather than separate copies.

**Legacy defects spotted in logic 3:** an average of 0 is treated as missing; the "Need to update Min Fillage Setpoint" message prints the wrong limit; `val_cycles_yest` and `val_rt_yest` can be `None` in comparisons; early `return` without a result.

## Additional gaps from logic 4: Idle Time Adjustment (fixed-speed controller)

| # | Gap | Legacy ACL need | Notes | Size |
|---|-----|-----------------|-------|------|
| G22 | Daily-snapshot quantities | Inferred production, cycles, run time and strokes for yesterday and the day before | `*Yesterday` fields are controller rollovers that change at gauge-off time, so history is read at the gauge-off time of each day. The adapter needs a way to ask for the value for a given day (or the last two days). `GetTrends` returns daily averages and may serve this (G5) | M |
| G23 | Inferred production and strokes quantities | `InferredProdYesterday`, `StrokesYesterday` | Extends G1. Check which exist in ForeSite's well status or daily records | S |
| G24 | Value with unit as a string | `IdleTime` arrives as a string in seconds; written in seconds | Setpoint reads need typed values with units (extends G4 and G2) | S |
| G25 | Site clock / gauge-off time | Reads at a configured gauge-off hour and minute in local time | Time zone: the host should supply the well's or site's time zone. Legacy uses the server's local time | S |
| G26 | Fixed-speed controller family | RPOC idle-time setpoint (`IdleTime`) | Another controller family for G19's per-device-type setpoint map | S |

**Intent:** tune a pump-off controller's idle time from yesterday's outcome. If inferred production held at or above a target percentage of the day before, then if cycles exceeded the desired count lengthen idle time by a step, otherwise leave it. If production fell, shorten idle time by a step. Always clamp to the min and max idle time. If any of the two days' values are missing, do nothing.

**Legacy defects spotted:** a value of 0 is treated as "missing" (a real zero, such as no strokes, blocks the logic); bare `except` hides read failures; a log line says "90 percent" regardless of the parameter; the no-data path ends without a result; local-time `astimezone` arithmetic can shift across daylight-saving changes; two reads per quantity where one range read would do.

## How each gap is addressed per provider

How the two adapters differ today:

| | ForeSite | CygNet |
|---|---|---|
| Data model | Well-centred REST documents (`Surveillance/well/{id}`, setpoint groups, alarms) | Point-centred: facility tag plus UDC, configured per connection (`CygNetQuantityMapping(Udc, Unit)`) |
| Quantity catalog | Fixed in code: 2 quantities, units forced | Default UDC map (`RRLPFILAVG`, `PRCASXIN`, `PRTUBXIN`, `RRLMXLDDLY`), overridable per site. Unmapped quantities fail with `CYGNET_QUANTITY_UNSUPPORTED` |
| Capabilities | `AssetDiscovery`, `CurrentData` | Current reads, via `ICygNetClient.ReadCurrentAsync` only |
| Simulator | None (G10) | `SimulatedCygNetClient` exists |
| Natural strengths | Calculated well status (cycles, runtime, ACL settings, intelligent alarms) | Point history, point writes, alarms on points |

Approach, gap by gap. "Neutral" is the contract change made once; the other columns are the per-adapter work.

| Gap | Neutral contract change | ForeSite implementation | CygNet implementation |
|---|---|---|---|
| G1 / G23 extra current quantities | Add quantities to the shared catalog: `SPM`, `CyclesYesterday`, `RunTimeYesterday`, `StrokesYesterday`, `InferredProdYesterday`. Catalog says which providers support each | Read from the well status document (`RRLWellStatusValueDTO`). Inferred production and strokes need confirming | Add default UDC mappings or require site mapping. Unmapped means a clear unsupported error, as today |
| G2 / G3 units, ranges, catalog | Catalog entry carries unit, range and description. Adapters report the unit they actually serve | Stop forcing `%` and 0-100 for everything | The mapping already has a unit. Add a range, and convert if the point unit differs |
| G9 one read per quantity | None (internal) | Cache the well document for one execution | Batch the point reads for one facility |
| G10 test double | Provider-neutral fixture set (values, history, setpoints) | Build a ForeSite simulator | Extend `SimulatedCygNetClient` to the same fixture set |
| G4 / G11 / G19 / G24 setpoint reads | New read capability: logical setpoint name to typed value with unit. Controller family (G13, G26) chooses the map | `GetWellAutonomousControlGroupData`, a per-device-type map | Setpoints are points: a UDC map per controller family, in the site mapping |
| G5 / G22 history and daily snapshots | `IHistoricalDataProvider` already defined. Need a daily-snapshot read with a time zone (G25) | `GetTrends`, daily averages only. Hourly is not available | Native point history, so hourly and sub-daily are possible |
| G12 `AverageSPM` | A catalog quantity | From well status or trends | An averaged point, or computed from history |
| G6 parameter pre-fill | Optional provider hook for suggested parameter defaults | `GetACLLogicSettings` | No native equivalent. Likely site configuration |
| G7 / G14 alarms | `IAlarmProvider` already defined. Neutral alarm shape: id, severity, active, since | `CurrentAlarms`, `ActiveIntelligentAlarms` | Point alarm status. No intelligent alarms |
| G8 recommendations and writes | `ICommandProvider` plus a recommendation shape, behind the design review (shadow versus live, approvals, verification) | `ApplyRecommendationForEachSetpoint`, `RunAutonomousControlTask`, `Well/Command` | Point write, then read back to verify |
| G13 / G26 device type and controller family | Asset attribute `controllerFamily`, visible to modules | From `WellControllerCapabilities` and device types | From facility attributes, or the UDC set present |
| G15 reference data (pump speed table) | A neutral reference-data read, or a module-bundled table | ForeSite configuration | Probably module-bundled |
| G25 time zone | Asset attribute `timeZone` | From the well or site | From the CygNet site or facility |

Gaps G16-G18 stay outside the adapters by design (persistence, closed loop).

Where the two adapters differ, the neutral contract takes the smaller common surface. A provider-specific extra is reported by a capability flag, not assumed. A module that needs a capability the connected provider lacks fails validation, not the run.

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
- **Target Fillage Phase 1 (Unico SPM):** the same as Phase 1 (VSD), expressed in SPM on a Unico controller, including paired up/down setpoints.
- **Idle Time Adjustment (fixed speed):** tune idle time from yesterday's outcome against the day before: lengthen when production holds and cycles are high, shorten when production falls, always within min and max.
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
