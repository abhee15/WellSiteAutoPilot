# ForeSite adapter reference

What the ForeSite adapter can call, which branch it lives on, and how each call was verified. ForeSite's API is the only data source. The adapter adds no tables and keeps no copy of ForeSite data (see `provider-logic-module-gaps.md`, the epic and tracker).

Auth for every call: `TokenService/CreateToken` with the Gateway Windows account, then the session cookie. A 401 re-creates the token once. Server base address and virtual directory come from the connection settings.

## On `develop` today

| Capability | Route | Verb | Notes |
|---|---|---|---|
| Connection check | `SurfaceNetworkService/Asset/All` | GET | `ForeSiteAdapter.CheckAsync` |
| Asset discovery: user assets | `Wells/UserAssets` | GET | scopes discovery to the account |
| Asset discovery: wells | `Wells/Well/Filter` | POST | a query, not a write; 10,000-well scale covered by tests |
| Asset discovery: device types | `WellConfig/GetWellAutonomousControlGroupData` | POST | a query; yields `deviceType` |
| Current quantity | `Surveillance/well/{id}` | GET | `PumpFillage`, `AveragePumpFillage`; timestamp is `LastGoodScanTime`, offset-aware, never `LastScanTime` |
| Latest dynacard | `Dynacards/Dynacard/LatestWithUnits/Lib/{id}/{type}` | GET | structured card value, size-capped |

Advertised capabilities: `AssetDiscovery`, `CurrentData`. Nothing else (no history, alarms, setpoints, commands, verification).

## On the POC branches (stacked, in this order)

| Gap | Branch | Adds | Route |
|---|---|---|---|
| G9 | `poc/gap-g09-single-status-read` | one status read per well, 3 s cache per session | `Surveillance/well/{id}` |
| G2 | `poc/gap-g02-provider-units-ranges` | quantity table; unit and range from the `Units` block | same |
| G1 | `poc/gap-g01-more-current-quantities` | `CurrentSPM`, `CyclesYesterday`, `RunTimeYesterday` | same |
| G12 | `poc/gap-g12-average-spm` | `AverageSPM` | same |
| G23 | `poc/gap-g23-daily-quantities-sentinel` | `CyclesToday`, `RunTimeToday`, `IdleTimeToday`, `IdleTimeYesterday`, `StrokesToday`, `StrokesYesterday` (65535 is missing), `InferredProductionToday`, `InferredProductionYesterday` | same |
| G3, G13 | `poc/gap-g03-discovered-catalog` | per-well catalog of reported quantities with units, ranges and `DeviceType` | same |
| G4 | `poc/gap-g04-readonly-setpoints` | read-only setpoints: id, description, group, value, unit, range, read-only flag | `Surveillance/Well/AllSavedSetPointParams?WellId=&scanOption=0` (GET) |
| G7, G14 | `poc/gap-g07-readonly-alarms` | active alarms: id, type, name, UTC start, value, deviation, suppressed | `Alarms/CurrentAlarms/{id}` (GET) |
| G5, G22, G25 | `poc/gap-g05-daily-averages` | daily values with UTC production-day edges, units | `Surveillance/Well/DailyAverage/{id}/{start}/{end}` (GET, ISO 8601) |
| G5b | `poc/gap-g05b-time-range-trends` | time-range history of any quantity (about 5 min points), unit per quantity, per-quantity errors | `Surveillance/CygNetTrends/{id}/{start}/{end}` (POST, quantity enum names in the body; read-only) |
| G10 | `poc/gap-g10-foresite-simulator` | offline simulator replaying recorded well 776 responses | test project only |

All POC reads are GET, read-only, and passed the ForeSite adapter checks. The routes for G4, G7 and G5 were also probed live on well 776.

## Known not to work, or not confirmed

| Route | Result | Consequence |
|---|---|---|
| `Surveillance/GetTrends/{id}/{start}/{end}` | 401 on GET and POST for well 776 | not used; use `Surveillance/CygNetTrends` for sub-daily history (G5b) |
| `WellAutoSetPointService/GetACLLogicSettings`, `GetRRLAutonomousControlHistory` | 401 on GET | route or verb unconfirmed; blocks G6 |
| `Surveillance/AvailableQuantities/{id}` | 200, but only enum ids, no names | not usable for the catalog on its own |

Verbs in the raw endpoint list gathered earlier were not reliable. Confirm each route's verb against `ISurveillanceService` and the other service interfaces before building on it.

## Not built

Setpoint writes and alarm set/clear (G7, G8), recommendations (G20, G21), parameter pre-fill (G6, G15), the per-controller setpoint map (G19), sub-daily history, intelligent-alarm detail, and the gateway or API route that exposes the catalog.
