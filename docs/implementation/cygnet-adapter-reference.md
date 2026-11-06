# CygNet adapter reference (live findings)

For builders without CygNet access. Verified from the dev host, read-only.

## Environment

- Working: domain **27212**, site `CYG_DEMO`, site service `CYG_DEMO.UIS`, facility `RPOC_0001` (137 points; the site has 2063 WELL facilities). Services up: UIS, VHS, HSS, PNT, FAC, DDS. CVS not found.
- Failing: domains 9077, 22567 (`DCL error -12`, no directory service) and 9033 (`-30`, CAL).
- Live client needs the CygNet managed libraries: `-p:CygNetLibraryPath=<folder>` or env `CYGNET_LIBRARY_PATH`. Do not reference Historian assembly types in a static field initialiser (the JIT loads them before `CygNetAssemblyResolver.Register()`); keep them in a nested static class (`Historians`).
- The CygNet API is synchronous: keep the bounded off-thread gate (10 s budget). Service lookups may take ~15 s.

## Calls used

| Job | Call |
|---|---|
| Find a service | `new ServiceInformation().GetAssociatedService(site.service, ServiceType.VHS / CAS / FAC / PNT)` |
| Catalog | PNT `ConfigClient.GetPointTagList("siteservice = 'S.SVC' AND facilityid = 'F'")` (single quotes doubled); point record gives UDC, description, primary units, enum text |
| Current value | CVS-style point value (adapter reads one point per call; batch per facility is a planned change) |
| History | VHS `GetHistoricalEntriesReverse(name, startUtc, endUtc, false)` |
| Alarms | CAS `Alarms.Client(cas).GetAlarmsForPoints(tags)` |
| Device type (not built) | DDS `GetDeviceProperty(deviceId, "DevType")` |
| Setpoints (not built) | DDS `GetLatestSetpointTransaction(dgType, relation, ordinal)`: passive; needs a per-device-type map (ForeSite: `CygNetWrapper\ControllerSetPointInfo` XML) |
| Writes (not built, design review) | `GetTokenizedDataGroupTransaction(...)` through a UIS command with a timeout |

## Identifiers

- Point tag / long id: `SITE.SVC:FACILITY_UDC`, e.g. `CYG_DEMO.UIS:RPOC_0001_RRLPFILAVG`. VHS needs the long id.
- Catalog key: `cygnet.udc.<UDC>`; display name = point description; unit = `PrimaryUnits` (empty = unitless).
- History entries: `entry.Value` is `byte[]`; decode by the point `ValueType`. Skip entries whose base status is `Unreliable` (counted separately). Cap 50,000 entries; window bounds required.

## Live catalog sample (RPOC_0001, excerpt)

```
cygnet.udc.DHPGAUGE   | Downhole Gauge Pressure | psi
cygnet.udc.PRCASXIN   | Casing Pressure         | psi
cygnet.udc.PRTUBXIN   | Tubing Head Pressure    | psi
cygnet.udc.PRESLINE   | Flowline Pressure       | psi
cygnet.udc.QTLIQT     | Inferred Prod Today     | bbl/D
cygnet.udc.QTLIQYD    | Inferred Prod Yesterday | bbl/D
cygnet.udc.QTLIQYDCHG | Inferred Production Chg | %
cygnet.udc.RATESTEAM  | Injection Rate          | MCFD
cygnet.udc.IDLTIMENEW | New Idle Time           | (unitless)
cygnet.udc.RRLALM0001 | RPC Control Fail        | (digital)
cygnet.udc.RRLALM0002 | Low Load Alarm          | (digital)
```

Quantity UDC families: fillage `RRLPFILAVG`/`RRLPFILCUR`; strokes `RRLSPMAVG`/`RRLSTKTDY`/`RRLSTKYEST`; cycles `RRLCYCTDY`/`RRLCYCYEST`/`RRLCYCTAVG`; run time `TMRUNT`/`TMRUNYD`; idle `RRLIDLTDY`/`RRLIDLYEST`; loads `RRLMXLDDLY`/`RRLMNLDDLY`; pressures `PRTUBXIN`/`PRCASXIN`/`PRESLINE`/`DHPGAUGE`; alarms `RRLALM0001..0168`.

Default map in the adapter: `PumpFillage`=`RRLPFILAVG`, `CasingPressure`=`PRCASXIN`, `TubingPressure`=`PRTUBXIN`, `RodLoad`=`RRLMXLDDLY`; per-connection `pointMap` overrides.

## History sample

`RRLPFILAVG`, 7 days to 2026-11-05: 30-minute entries, 48 per day (50 on 11-01 across a boundary), average 99.67, 0 unreliable. `TMRUNYD` is a daily point (no entries in 3 days). Only `RRLPFILAVG` had fresh data on the test site; some points are stale since 2023, so surface the timestamp and apply freshness rules.

## Alarms

CAS returned a set alarm on `RRLPFILCUR` dated 2023 (stale). Alarm state otherwise lives in the `RRLALM*` digital points: treat them as a point group and query CAS for active alarms.

## Quirks

- `RRLSTKYEST` = 65535 is an unset sentinel (same as ForeSite `StrokesYesterday`): report missing.
- No single `IdleTime` point; derive from idle UDCs or time-in-state points.
- Values on the test CygNet match ForeSite well 776 only because ForeSite is fed from it. Do not generalise.

## Open

Device type, setpoints, relaxing `AdapterReadRules.CygNetQuantities` for `cygnet.udc.*`, daily rollups (`GetHistoricalRollup`, `GetDailyRollupByPointName`), well tests (`CygNet.API.WellTest`), writes. ForeSite's reference implementation: `ForeSiteService\Server\Support\CygNetWrapper\`.
