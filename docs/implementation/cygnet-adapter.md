# CygNet connection adapter

Adapter key `cygnet`: capabilities `AssetDiscovery` and `CurrentData`. Facilities become assets, grouped by CygNet
category and facility type. Reads use the standard authorized read plan via Gateway `POST /adapters/cygnet/current`.

## Connection settings

| Key | Meaning |
|---|---|
| `domain` | CygNet domain id (e.g. `27212`) |
| `siteService` | UIS `site.service` (e.g. `CYG_DEMO.UIS`); the FAC service is resolved from its association |
| `facilityFilter` | optional wildcard on facility id (e.g. `RPOC_00*`) |
| `pointMap` | optional per-connection override: `Quantity=UDC\|unit;...` |

## Backends (`Providers:CygNet:Backend`)

- `unconfigured` (default): the adapter reports unavailable; no data is ever invented.
- `simulated`: deterministic wells (`WELL_001..005`, a battery, a tank) for offline work and CI.
- `live`: CygNet.API-backed, opt-in. Set `CYGNET_LIBRARY_PATH` to the CygNet library folder before building the
  Gateway (see `src/integration/WellSiteAutoPilot.Integration.CygNet/README.md`). The main solution builds without it.

Default quantity to UDC map: PumpFillage `RRLPFILAVG` %, CasingPressure `PRCASXIN` psi, TubingPressure `PRTUBXIN` psi,
RodLoad `RRLMXLDDLY` lbf. MotorAmps is unmapped by default (`CYGNET_QUANTITY_UNSUPPORTED`) and needs a `pointMap` entry.
Gateway overrides: `Providers__CygNet__Quantities__<Quantity>__Udc` / `__Unit`.

Server read-source checks use the adapter-declared rules in `AdapterReadRules` rather than hard-coded ForeSite checks.

## Sample modules

`modules/samples/cygnet/`: `pump-fillage-low-alert`, `pressure-differential-check`, `motor-load-check`
(current-data only, so the pressure check is a point-in-time casing/tubing differential). `pack-samples.ps1` publishes
and packs them with the CLI.

## Verification

- Offline: `dotnet run --project tests\WellSiteAutoPilot.CygNetAdapterTests` (descriptor, settings, point map,
  check, discovery/filter/truncation, reads, error redaction, the three modules).
- Live (CYG_DEMO, domain 27212, UIS), via a scratch harness over the real client and adapter: check Healthy;
  discovery of 2063 facilities in about 1.3 s; filter `RPOC_00*` gives 107; PumpFillage 99.67 % Good, casing/tubing
  pressure, RodLoad read; `pointMap` override honoured (RRLPFILCUR reads Uncertain, ConfigurableBit1).
- Not verified: Server import and shadow run of a CygNet module. The Aspire AppHost stalled at DCP start on this
  machine (the POC needed a local DCP pin) and the worker on this branch only resolves built-in modules.

## Known limits

- All connections use the Gateway's Windows account, which needs a CygNet CAL.
- The CygNet API is synchronous; a timed-out read leaves a thread running (mitigated by client caching).
- Some demo points carry old timestamps; module `maximumAgeSeconds` must allow for that.
