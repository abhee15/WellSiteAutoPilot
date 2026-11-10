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

## Backend

CygNet connections work like ForeSite connections: each one names its own domain and site.service, and the Gateway connects
to that real CygNet system. There is no simulated CygNet and no backend setting. The Gateway uses the CygNet client components already installed on its server (CygConn, CExplorer or a CygNet server install);
WSA never installs, registers or ships CygNet, and no special build is needed. It finds the CygNet API folder, in order, from:
the Gateway setting `CygNet:LibraryPath` (appsettings or `CygNet__LibraryPath`), the `CYGNET_LIBRARY_PATH` environment variable,
the folder of the registered CygNet COM server (`CxCvsLib.CvsClient`), then `C:\CygNet\Support64` and `C:\CygNet\Support`. A folder
counts only if it holds `CygNet.API.Facilities.dll`. A laptop without CygNet can still add a connection: the test runs on the
Gateway server, and a server without CygNet reports that CygNet is not installed there. If the CygNet server cannot be reached
the connection reports `Connection error: a connection to CygNet domain N cannot be established`. The only simulator is
the Provider Simulator; the CygNet offline tests use a test double that lives in the test project.

Default quantity to UDC map: PumpFillage `RRLPFILAVG` %, CasingPressure `PRCASXIN` psi, TubingPressure `PRTUBXIN` psi,
RodLoad `RRLMXLDDLY` lbf. MotorAmps is unmapped by default (`CYGNET_QUANTITY_UNSUPPORTED`) and needs a `pointMap` entry.
A connection's `pointMap` replaces or adds to the defaults; there is no Gateway-wide override.

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

## Connection failures (verified live)

CygNet.API raises one `MessagingException` ("Failed to retrieve associated FAC service for [domain]site.service") for an unknown
domain, an unknown site.service and an unreachable directory service, so the adapter cannot tell them apart and reports all three
as `Unreachable` with one message naming the site.service and the checks to make (server running and reachable, domain registered
on the Gateway machine, site.service correct). Seen on this host: an unknown site fails in 0.1 s; an unregistered or unreachable
domain takes 10 to 21 s, longer than the 10 s test limit, so the test reports the domain-level connection error. The vendor text
stays in the inner exception. An account that CygNet refuses is still reported separately (`UnauthorizedAccessException`); that
case has not been reproduced live yet.

## Known limits

- All connections use the Gateway's Windows account, which needs a CygNet CAL.
- The CygNet API is synchronous; a timed-out read leaves a thread running (mitigated by client caching).
- Some demo points carry old timestamps; module `maximumAgeSeconds` must allow for that.

## Failure reasons, freshness and point mapping

- `CYGNET_POINT_NO_VALUE`: the point exists but never received a value (blank record, e.g. CYG_DEMO RPOC_00003).
  `CYGNET_VALUE_NOT_NUMERIC`: the point holds text or a state; map the quantity to a numeric point.
  `CYGNET_POINT_NOT_FOUND`: no such UDC on the facility. `CYGNET_BUSY`: earlier calls to the service are still hung.
- The connection test and discovery sample up to 3 facilities and report each quantity's data age, missing points and
  unmapped quantities. A `pointMap` entry that matches no sampled facility makes the test "needs attention".
  The discovery note is a warning (`UnavailableFields` is declared empty so import grouping is unaffected).
- **Recommended pattern for stale data:** read the age from the connection test, then set the module's
  `maximumAgeSeconds` just above it. Do not use years-long ages in production; point the connection at live points
  instead. On CYG_DEMO, RPOC_0001 has current fillage/rod load but 2023 pressures; RPOC_00001..3 are from 2020.
- CYG_DEMO has no motor-current point (only `RRLSPMAVG`, strokes per minute), so MotorAmps stays unmapped there.
- Bounded calls: one cached FAC and realtime client per domain/service; at most 4 concurrent CygNet calls per service,
  and a timed-out call keeps its slot until it returns, so a hung service cannot pile up threads.
- Discovery of more than 5000 facilities is reported incomplete with a warning; import is refused until the facility
  filter narrows it. External ids are `SITE.SERVICE::FACILITY`; bindings are per connection, so equal ids in other
  services or domains do not collide.