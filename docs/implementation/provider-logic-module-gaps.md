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
| G5 | No history | Averages of SPM and fillage over the last N hours | `Surveillance/Well/DailyAverage/{well}/{start}/{end}` (GET) returns daily values only; `Surveillance/GetTrends` returned 401 in our probe. Hourly history may exist only via CygNet. Needs a decision | M-L |
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

## ForeSite API findings (probed read-only on the test server, 2026-11-05)

Access: `GET TokenService/CreateToken` as the Windows account, then the session cookie on every call. Routes carry the service prefix: `Surveillance/`, `Wells/`, `WellConfig/`, `WellTests/`, `Alarms/`, `IntelligentAlarm/`. Several read-only queries are POST (`OptionalQuantities`, `CygNetTrends`, `DailyAverageQuantities`).

| Family | Endpoint (examples) | What it gives | Self-describing? |
|---|---|---|---|
| Quantity availability | `Surveillance/AvailableQuantities/{wellId}` | Ids of the `WellQuantity` enum (~75 members) valid for that well. Well 1596 (AEBEAM) has no fillage (15/16); well 776 (WellPilotRPOC) has | Ids only. Names and units come from the enum and the status document |
| Current status | `Surveillance/well/{id}` | Every status quantity plus a `Units` block (unit key, precision, min, max) | Yes |
| KPI and gauges | `Surveillance/KPI/{id}`, `Well/Gauges/{id}` | Inferred production, SPM, cycles; gauge values with unit and range | Yes |
| Site-defined quantities | `Surveillance/OptionalQuantities/{wellType}`, `CalculatedQuantities/{wellType}` | Name, UDC, unit, unit category, point type | Yes. Already site-configurable in ForeSite |
| Daily history | `Surveillance/GetTrends/{id}/{from}/{to}`, `Well/DailyAverage/...` | Daily averages and volumes with units | Yes |
| Point history by quantity | `POST Surveillance/CygNetTrends/{id}/{from}/{to}` with quantity names | History of any mapped `WellQuantity`, with units, from the well's CygNet point. An unmapped quantity returns a blank UDC | Yes. No data on the test CygNet, so not yet verified with values |
| Setpoints | `Surveillance/Well/AllSavedSetPointParams`, `SetPointsDGs` | Element id, description, unit, range, read-only flag, enum, value and original value, by data group | Yes. `IdleTime` shows `00:01:00` and original `60` (G24). Live-read variants need a UIS command and failed on stale wells |
| Alarms | `Alarms/CurrentAlarms/{id}`, `Surveillance/GetCygNetAlarms/{id}`, `IntelligentAlarm/...` | Type, value, start time | Partly |
| Well tests | `WellTests/GetLatestWellTestData`, `WelltestQuantities/{wellType}`, `WellTestTrend/...` | Test results, test quantity catalog by well type | Yes |
| Config and logic | `WellConfig/{id}`, `GetLogicModuleList/{id}` | Well attributes, available ACL logic modules | Yes |
| Autonomous control | `WellAutoSetPointService/GetACLLogicSettings`, `GetRRLAutonomousControlHistory`, `ApplyRecommendationForEachSetpoint` | Logic settings, run history, applying recommendations | To be probed |

Device-type differences matter: the quantity set varies by well type and device, so a fixed list cannot be right.

Finding wells to test with: `POST Wells/WellFilter` (body `[null]`) returns the filter options. `POST Wells/Well/Filter` takes a `WellFilterDTO`, for example `{"AssetIds":[],"welScadaTypeValues":[{"Value":"CygNet"}],"welDeviceTypeValues":[{"Value":"WellPilotRPOC"}]}`. On the test server (1,403 wells):
- SCADA types: Concentrator, CygNet, None.
- Device types: AEBEAM, LufkinSam, SAMIWC, WellPilotRPOC, WFordKS, WFTA1K.
- Well types: CInj, ESP, GInj, GLift, NF, OT, PCP, PLift, RRL, SInj, WGInj, WInj.
- Logic modes: Autonomous, Recommendation. The configured logic list includes `RRL_AEBEAM Logic`, `RRL_LufkinSam Logic`, `RRL_RPOC Logic`, `RRL_SPM Optimization Logic (Well Pilot)` and `RRL_VSD Optimization (LufkinSAM)`.

Wells seen with live data (last scan 2026-11-05): 776 `RPOC_00016+9077` (WellPilotRPOC; fillage 99.6, SPM about 10, setpoints present), 457, 488, 732, 715 (WellPilotRPOC); 833, 995, 1520 (LufkinSam); 1609 AEPOC2 (AEBEAM). Well 1596 `AR-RRL1` (AEBEAM) has no fillage and no saved setpoints.

Survey by SCADA type CygNet and device type (wells with the device type; sample of up to 60 read for status):

| Device type | Wells | Well types seen | Status fields populated |
|---|---|---|---|
| AEBEAM | 6 | RRL | Inferred production only (420). No fillage, strokes or cycles in most. `RPOC_ASU` (1680) has fillage 0 |
| LufkinSam | 302 | RRL, NF, PCP | Mostly empty. RRL `new well RRL test` (968) shows zeros |
| WellPilotRPOC | 344 | RRL, NF, GLift | Rod-lift wells such as 487 and 776 have fillage about 99.6 and inferred production. Others empty |
| WFordKS | 147 | GInj, ESP | Status empty |
| WFTA1K | 8 | PCP | Status empty |
| SAMIWC | 5 | WInj, WGInj | Status empty |

The device type is not the same as the well type: one device type (LufkinSam, WellPilotRPOC) spans several well types. A catalog must key applicability on both, plus `AvailableQuantities` for the specific well. Only a few rod-lift wells have usable fillage, which sets the limit for live tests: use 487, 776 and 457 (WellPilotRPOC), and 1680 (AEBEAM) for the zero and empty cases.

Observed gaps in the data, which a catalog has to tolerate:
- `StrokesYesterday` reads 65535 on WellPilotRPOC wells, which looks like an unset sentinel, and is empty on AEBEAM wells.
- Fillage fields are null for AEBEAM.
- Test-server CygNet returned no point history, so history needs a server with archived data.

Probe tooling (not in the repo): a PowerShell session helper that creates the token and calls any path, plus the extractor for the service interfaces. A copy is kept with the session files.

ACLM service: a gRPC Windows service that runs each logic as a Python or PowerShell child process, with a timeout, and captures its output. It holds no business logic. The Python helper library the legacy logics import (`servicefactory`, `models`, `constants`) is not in the ACLM tree and still needs to be located; it defines the API those logics actually used.

## Catalog design: a discovered quantity catalog instead of a fixed list

Direction to discuss (not yet decided):

1. **Discover instead of declare.** For a connection and asset, the adapter asks the provider what exists, and returns a catalog of entries: key, display name, data kind, unit, range, applicability. ForeSite sources: `AvailableQuantities`, the `Units` block, `OptionalQuantities`, `CalculatedQuantities`, `WelltestQuantities`, and setpoint groups. CygNet sources: the site's UDC mapping.
2. **Data kinds, not one list.** Each entry says what it is: `current` (status), `daily` (daily average or snapshot), `history` (point history), `welltest`, `setpoint`, `alarm`, `card`. A module's requirement then names the kind, the key and the window, for example "daily `InferredProductionYesterday` for the last 2 days".
3. **Stable keys with a namespace.** Core keys are shared across providers (`PumpFillage`, `CurrentSPM`). Site or provider-specific entries are namespaced (`foresite.optional.VOLSTEAMY`, `cygnet.udc.PRCASXIN`). Modules that use only core keys stay portable.
4. **Applicability from the asset.** Well type, device type and what `AvailableQuantities` returns decide which entries show for an asset. The UI and `module validate` use it to reject a module whose requirement the asset cannot meet.
5. **Units come from the provider.** The adapter reports the unit it received and the module can ask for a conversion, rather than the adapter forcing one.
6. **Cache and batch.** One status document read serves every current quantity of a well in one execution.
7. **Bounded freedom.** Arbitrary queries are limited to catalog entries and bounded windows and counts, so a module cannot pull arbitrary data or overload the provider.

Open questions: how a module declares "any of these keys" for different device types; how the catalog is versioned and cached; whether setpoints are in the same catalog or a separate one; how `Gateway` identity limits what the catalog shows.

## CygNet findings (where we are)

Sources: the CygNet adapter code, and ForeSite's own CygNet wrapper (`ForeSiteService\Server\Support\CygNetWrapper`), which shows the managed CygNet API calls ForeSite uses for the same jobs.

**Live probing works against domain 27212 only.** Domains 9077 and 22567 fail with `DCL error (-12)` (no directory service reachable) and 9033 with `-30` (CAL). The API calls below come from ForeSite's source, and the 27212 results at the end of this section confirm the point, units and history calls from the WSA host.

**Today**
- Capabilities: asset discovery (facilities) and current data (one point per call).
- Quantities: five, mapped to UDCs by a built-in map (`PumpFillage`=RRLPFILAVG, `CasingPressure`=PRCASXIN, `TubingPressure`=PRTUBXIN, `RodLoad`=RRLMXLDDLY, `MotorAmps` has none). A per-connection `pointMap` overrides it.
- Reads go through a bounded, off-thread gate (the API is synchronous), with a 10 s budget and quality mapped from status flags.
- Not implemented: history, alarms, setpoint reads or writes, UDC discovery.

**Needed (CygNet source per gap)**

| Gap area | CygNet source (as ForeSite does it) | Status |
|---|---|---|
| Catalog (G3) | `Points.ConfigClient(PNT).GetPointTagList("siteservice = 'S.SVC' AND facilityid = 'F'")` lists every point on a facility; each tag carries its UDC | In ForeSite source; not run from WSA |
| Units, description (G2) | `ConfigClient` point record: primary and alternate units, description, enum table, digital on/off strings | In ForeSite source |
| Current value (G1, G9) | `RealtimeClient(CVS).GetPointValueRecord(tag)`; batch variant reads many tags (`GetPointValueRecordsInner`) | Single read in the adapter; batch in ForeSite |
| History (G5, G22) | `Historian.Client(VHS)`: `GetHistoricalEntriesReverse(name, start, end)` and `GetHistoricalRollup(name, start, end, type, unit, period, topSubUnit)`; entries carry a base status (`Unreliable` is filtered) | In ForeSite source; ForeSite adds a lookback to carry the prior sample into a window |
| Alarms (G7) | `Alarms.Client(CAS).GetAlarmsForPoints(tags)`, found through `ServiceInformation.GetAssociatedService(site, CAS)` | In ForeSite source |
| Setpoint reads (G4, G11) | Not a point read. A setpoint group is a device data group fetched with a UIS command and stored as a DDS transaction; `GetLatestSetpointTransaction(dgType, relation, ordinal)` reads the last stored one, so it is passive | Per device type, from an XML map (`ControllerSetPointInfo`: group name, get and send command names, shown and hidden elements, partial-write flag, ordinal) |
| Setpoint writes (G8) | `GetTokenizedDataGroupTransaction(dgType, ordinal, time, uisCommandParam, relation, timeout)` sends the group to the device through the UIS command, with a timeout. Needs the design review | In ForeSite source |
| Device type (G13) | `DdsClient.GetDeviceProperty(deviceId, "DevType")` through the DDS service | In ForeSite source (a COM-based client) |
| Daily values (G22, G23) | UDC-named points, read through the same history calls with daily rollups (`GetDailyRollupByPointName`, `GetDailyRollupLastForAnalogs`) | In ForeSite source |
| Well tests | `CygNet.API.WellTest` assembly and `CygNetAutomatedWellTestHelper` | Not examined |
| Quantity to point mapping | ForeSite maps a `WellQuantity` to a UDC per mapping id (`GetNames`, `GetHistoricalPointNamesByFacility`), with site-configurable optional UDCs | In ForeSite source |

**Differences from ForeSite**
- CygNet is the raw point layer: no `AvailableQuantities`, so applicability comes from which UDCs exist on the facility.
- Quantity names are UDC ids, so catalog keys use the `cygnet.udc.<UDC>` namespace and optionally a mapping to the stable core keys.
- ForeSite already does the quantity-to-point mapping, so its mapped point ids (from `CygNetTrends`) can seed the CygNet map.

**What this means for the design**
- A CygNet catalog can be discovered: list the facility's points, then read units and description from the point records. No fixed list is needed. Applicability is "the point exists".
- History and alarms are separate CygNet services (VHS, CAS), each found through the directory. The adapter needs a service lookup per service type, not only the facility service.
- Setpoints are the biggest difference from ForeSite. ForeSite's `Well/AllSavedSetPointParams` returns a typed, self-describing view. CygNet gives raw data-group transactions per device type, so a provider-neutral setpoint read needs the per-device-type group map (the same one ForeSite uses) or ForeSite as the source. This is a strong case for a ForeSite-first setpoint read and a later CygNet read.
- Writes go through device transactions with timeouts and can fail on the device, so G8 stays a design-review item for both providers.
- The CygNet API is synchronous and some calls are slow (ForeSite gives service lookups a 15 s timeout), so the existing bounded gate stays, and history windows must be bounded.

**Live probe results: domain 27212, site `CYG_DEMO`, facility `RPOC_0001` (read-only, from this host)**

Domain 27212 works (UIS, VHS, HSS, PNT, FAC, DDS up; CVS not found). Domains 9077 and 22567 still fail with `-12`; 9033 with `-30`. The site has 2063 WELL facilities; `RPOC_0001` has 137 points.

| Finding | Evidence | Design implication |
|---|---|---|
| Quantity UDCs are discoverable per facility | Fillage `RRLPFILAVG`/`RRLPFILCUR`; strokes `RRLSPMAVG`/`RRLSTKTDY`/`RRLSTKYEST`; cycles `RRLCYCTDY`/`RRLCYCYEST`/`RRLCYCTAVG`; run time `TMRUNT`/`TMRUNYD`; idle `RRLIDLTDY`/`RRLIDLYEST`; loads `RRLMXLDDLY`/`RRLMNLDDLY`; pressures `PRTUBXIN`/`PRCASXIN`/`PRESLINE`/`DHPGAUGE` | Build the catalog from the point list, not a fixed map (G1, G9) |
| Units come from the point record | `RRLPFILAVG` primary `%`; `TMRUNYD` primary `second`; counts have empty units | Take units from `PrimaryUnits`; do not force them (G2). Empty means unitless |
| Descriptions are usable labels | "Average Pump Fillage", "Cycles Yesterday", "RPC Control Fail" | Use as catalog display names |
| Unset sentinel | `RRLSTKYEST` = 65535, same as ForeSite's `StrokesYesterday` | Treat 65535 as missing for count UDCs |
| Stale points | Some timestamps are from 2023 | Surface the timestamp and apply freshness rules |
| History works | VHS long id `SITE.SVC:FAC_UDC` returns 30-minute entries for fillage and cycles; `TMRUNYD` had none in 3 days (daily point) | Entry values are `byte[]`, so decode them with the point type. Window bounds are required |
| Alarms | CAS `GetAlarmsForPoints` returned 0 records; alarm state otherwise lives in `RRLALM0001..0168` digital points | Treat alarm points as a point group, and query CAS for active alarms |
| Daily and yesterday values | Plain UDC points, no single IdleTime point | Derive idle time from the idle UDCs or the time-in-state points |

Probe harness: `poc-cygnet\Probe2` (`Probe2 <domain> <site> <service> <facId> <udc,...>`: config, history, alarms).

**Remaining probes**
1. DDS device type and the setpoint data-group transaction (passive latest read only).
2. Compare with ForeSite if domain 27212 maps to a ForeSite well.
3. Domains 9077, 22567 and 9033 need a directory route and a CAL before they can be tested.

## ForeSite adapter: recommended build

Inputs: the adapter as built (`AssetDiscovery` + `CurrentData`, two hard-coded quantities, `%` forced, 0-100 enforced, one HTTP call per quantity), the API findings above, the four reference logics, and the sample module `demo.foresite-low-fillage-flag` (reads `PumpFillage` and `AveragePumpFillage` only).

**Keep.** Windows-identity session and token handshake, asset-scoped discovery (never `Wells/All`), the response-size and depth bounds, the timestamp rule (`LastGoodScanTime`, never retrieval time), and the `ForeSiteMeasurementException` error codes. The existing sample module keeps working unchanged.

**Change.**
1. **One status read per execution, data-driven field map (G9, G1, G23, G12).** `Surveillance/well/{id}` already returns every status quantity with a paired `Units` block. Replace the two-entry `switch` with a table of `{catalogKey, valueField, unitField}` and read the document once per run. Verified on well 776 (WellPilotRPOC):

   | Catalog key | `Value` field | `Units` field | Live value, unit |
   |---|---|---|---|
   | `PumpFillage` | `CurrentPumpFillage` | `CurrentPumpFillage` | 99.6 % |
   | `AveragePumpFillage` | `AveragePumpFillage` | same | 99.67 % |
   | `CurrentSPM` | `CurrentSPM` | same | 12.3 SPM |
   | `AverageSPM` | `AverageSPM` | same | 11 SPM |
   | `CyclesToday` / `CyclesYesterday` | same | none (count) | 0 |
   | `StrokesToday` / `StrokesYesterday` | same | none (count) | 9683 / 65535 (sentinel) |
   | `RunTimeToday` / `RunTimeYesterday` | same | same | 74 s / 24540 s |
   | `IdleTimeToday` / `IdleTimeYesterday` | same | same | 82734 s / 61860 s |
   | `InferredProductionToday` / `Yesterday` | same | same | 0.5 / 106.4 bbl/d |
   | `EffectiveRunTimePercentage` | same | same | 0.09 % |
   | `DailyMinimumLoad` / `DailyMaximumLoad` | same | same | 0 / 23739 lb |
   | `TubingPressure`, `CasingPressure`, `DHGaugePressure`, `TubingHeadTemperature` | same | same | psia, psia, psia, F |

   Idle time and `*Yesterday` values are already in the status document, so logic 4 needs no history for its inputs (G22 reduces to "read the yesterday fields"). Count fields have no unit block entry: report them unitless.
2. **Units and ranges from the provider (G2).** Take `UnitKey`, `Precision`, `Min` and `Max` from the `Units` block. Validate against that range when present, not a fixed 0-100. Missing range means no range check.
3. **Missing and sentinel handling (G23).** Null means `MISSING`. `65535` on a count field is reported as missing, with the raw value kept in the result for diagnosis. A real `0` is a value, so logics must not treat zero as missing (a legacy defect in logics 3 and 4).
4. **Discovered catalog (G3).** Add `ProviderCapabilityKind` catalog discovery per asset. Candidates are the intersection of `Surveillance/AvailableQuantities/{wellId}` and the field table, plus site-defined entries from `OptionalQuantities/{wellType}` and `CalculatedQuantities/{wellType}`, namespaced `foresite.optional.*`. Keys outside the table are exposed only when the provider gives a unit and the value is numeric.
5. **Device and well-type applicability (G13).** Already in discovery metadata (`deviceType`, `wellType`). Expose them to the catalog so a requirement can be rejected at validation for an AEBEAM well that has no fillage.
6. **Setpoints (G4, G11, G19, G24, G26), read-only first.** `Surveillance/Well/AllSavedSetPointParams?WellId=&scanOption=0` is self-describing (id, description, unit, range, read-only flag, value, original value, data group). Expose a neutral "setpoint read": logical name to typed value with unit, with a per-controller-family map (VSD, SPM/Unico, RPOC idle time) held as data, not code. `IdleTime` returns `00:01:00` with an original of `60`, so parse by the declared unit.
7. **Daily and history (G5, G22).** `GetTrends` and `DailyAverage` for day-level values. For finer windows use `POST CygNetTrends` once a server with archived data is available. Until then advertise `HistoricalData` only for the daily kind.
8. **Alarms (G7, G14).** `Alarms/CurrentAlarms/{id}` and intelligent alarms as a read-only `Alarms` capability. The status document also carries `CurrentAlarmsForWell`, `UnsuppressedAlarms` and `IntelligentAlarmMessage`.
9. **Simulator (G10).** A fake ForeSite that serves the 776 status document, units, `AvailableQuantities` and a setpoint group, recorded from the live responses. It is what CI runs.
10. **Writes last (G8, G20, G21).** `WellAutoSetPointService` (`ApplyRecommendationForEachSetpoint`) behind the governed design (Shadow, recommend, approved write, readback). `GetACLLogicSettings` and `GetRRLAutonomousControlHistory` returned 401 on well 776 with GET (a 401 can also mean wrong verb or route), so they need the route and verb confirmed from the service interface before any use.

**Why this shape.** The status document is the single source ForeSite already computes from the controller, so reading it once and fanning out gives most of the four logics' inputs at the cost of a table plus units handling. Setpoint reads and alarms are separate read-only capabilities. Writes are the only part that needs a design review. Nothing here needs the legacy Python helper library.

**Cross-adapter check (test-environment artifact only).** Well 776 shows `CurrentPumpFillage` 99.6, `AveragePumpFillage` 99.67 and `AverageSPM` 11, the same as CygNet `CYG_DEMO` `RPOC_0001`. This is because our test ForeSite is fed by that CygNet system, which we reused instead of building two separate CygNet systems. It is **not** a property of the product: customers connect to ForeSite or to CygNet, or to two systems that share no assets, so values will not match across adapters. Do not assert equivalence between adapters in tests or design. The useful part is only that, in this environment, a ForeSite-side read can be sanity-checked against its source CygNet points (field mapping, units, sentinel handling). Provider-neutral behaviour is proven by running the same module against each adapter's own fixture or simulator, and comparing the module's behaviour, not the data.

**Build order for the ForeSite adapter (one PR each):** (a) field table, one read, provider units and ranges, sentinel handling (G1, G2, G9, G23); (b) discovered catalog plus applicability (G3, G13); (c) read-only setpoints with the per-family map (G4, G11, G19, G24); (d) alarms (G7, G14); (e) daily values (G5, G22); (f) simulator (G10, can land with (a)); (g) writes (G8, G20, G21).

## Gap tracker (epic)

One POC branch per gap, in `origin`. Code-gap branches are **stacked** in the order shown (each is based on the one above it) because they touch the same ForeSite files; review and re-implement them in that order. All base on `origin/develop` at `b1e45e06` (#477). Status values: **POC done** (built and checked with the ForeSite adapter checks), **Planned**, **Design only** (no code in the adapters), **Not a requirement**.

| Gap | Branch | Status | Notes |
|---|---|---|---|
| G9 one status read | `poc/gap-g09-single-status-read` | POC done | `ForeSiteStatusCache`, 3 s default lifetime per connection session, off when zero. Test: two quantities, one GET |
| G2 provider units and ranges | `poc/gap-g02-provider-units-ranges` | POC done | `ForeSiteQuantityCatalog` table. Unit, min and max from the `Units` block; catalog fallbacks only when absent |
| G1 more current quantities | `poc/gap-g01-more-current-quantities` | POC done | `CurrentSPM`, `CyclesYesterday`, `RunTimeYesterday`. A real zero stays a value |
| G12 `AverageSPM` | `poc/gap-g12-average-spm` | POC done | From the status document |
| G23 daily quantities and sentinel | `poc/gap-g23-daily-quantities-sentinel` | POC done | Today and yesterday cycles, run time, idle time, strokes, inferred production. 65535 strokes is missing |
| G3 discovered catalog, G13 device type | `poc/gap-g03-discovered-catalog` (`8c3d395c`) | POC done (adapter level) | `ForeSiteCatalog.ReadAsync` lists only quantities the well reports with a real value, with units and ranges from the `Units` block, plus `DeviceType`. Gateway/API exposure and `OptionalQuantities` are not done |
| G4, G11, G19, G24, G26 setpoint reads | `poc/gap-g04-readonly-setpoints` (`099d739c`) | POC done (read-only) | `ForeSiteSetpoints.ReadAsync`: GET `Surveillance/Well/AllSavedSetPointParams?WellId=&scanOption=0`, live-probed on well 776 (groups, `ElementId`, string `Value`, `UnitKey`, `RangeLow/High`, `IsReadOnly`). Element ids stay controller-native; the per-device-type map is still to do (data, not code) |
| G5, G22, G25 history, daily snapshot, time zone | `poc/gap-g05-daily-averages` (`65b467d4`) | POC done (daily only) | `ForeSiteDailyAverageReader`: GET `Surveillance/Well/DailyAverage/{id}/{start}/{end}` (ISO 8601), live-probed. Day edges come back as UTC (05:00Z for a Central well), so the asset needs a time zone attribute. `Surveillance/GetTrends` returned 401 on both GET and POST for 776 and is not used. Sub-daily history is still open |
| G7, G14 alarms | `poc/gap-g07-readonly-alarms` (`d965f647`) | POC done (read-only) | `ForeSiteAlarms.ReadAsync`: GET `Alarms/CurrentAlarms/{id}`, live-probed. Intelligent-alarm detail and set/clear are not done |
| G10 ForeSite simulator | `poc/gap-g10-foresite-simulator` (`27d822f3`) | POC done | Test-project `ForeSiteSimulator` serving fixtures recorded read-only from well 776 (well name sanitized): status, setpoints, alarms, daily averages. `SimulatorChecks` runs every adapter read against it. Only well 776 exists |
| G6 parameter pre-fill, G15 reference data | not started | Design only | Needs the ACL settings route (401 on GET) and a decision on module resources |
| G8, G20, G21 recommendations and writes | not started | Planned last | Governed design first |
| G16, G17, G18 | none | Not a requirement | Legacy persistence, closed loop, manual-change detection |

Workflow note: the first stacked branches were rebuilt once because commits raced with edits; the pushed history is the corrected one.

### When each kind of data is fetched (proposed)

| Data | When | Reason |
|---|---|---|
| Identity and attributes (id, name, `deviceType`, `wellType`, controller family, time zone) | Asset import, refreshed by re-sync | Rarely changes; modules and validation need it before any run. Discovery already carries `deviceType` and `wellType` |
| Quantity catalog (G3) and setpoint list (G4) | On demand when authoring or validating a module, cached per asset with a TTL and manual refresh | Depends on what the well reports now; reading it for every well at import is slow at 10,000-well scale and goes stale when a controller changes |
| Current values, setpoint values, active alarms | At run time, one status read per well (G9) | Must be fresh |
| Daily history (G5) | At run time, only the window a module asks for | Day boundaries need the asset time zone (G25) |

Open: the gateway/API route, cache, refresh trigger and storage for the catalog are not designed; the time zone is not yet an asset attribute.

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
