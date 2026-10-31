# CygNet adapter POC: findings and handoff

Branch: `poc/cygnet-reader`, built on `feature/cygnet-connection-and-facilities` (#311, which sits on #303).
This is POC code. It is not for merge as is; it exists so the real changes can be made through GitHub.

## Result
Against CygNet domain 27212 (site `CYG_DEMO`), on a box with a CygNet licence and the ForeSite CygNet libraries:

1. A CygNet connection (`domain`, `site`, `service=UIS`) tests Healthy.
2. Discovery lists facilities (for example `RPOC_0001`, "RPOC Well 0001").
3. Three wells were imported as assets (`kind: cygnet.facility`).
4. A configured logic (a CygNet copy of the well-input-check module, `requiredAdapterKey: cygnet`) was defined on `RPOC_0001`, validated, activated and run in Shadow.
5. The run completed with `WELL_INPUT_CHECK_COMPLETED`: PumpFillage 99.67 %, quality Good, source `cygnet`. The value is the CygNet point `RRLPFILAVG` on `CYG_DEMO.UIS::RPOC_0001`.

## What this branch adds
| Change | Where |
|---|---|
| CygNet-backed `ICygNetFacilityReader` (check + list facilities) | `Integration.CygNet/CygNetFacilityReader.cs` |
| Per-connection current-value read and Gateway route `POST /adapters/cygnet/current` | `Integration.CygNet/CygNetConnectionMeasurements.cs`, `IntegrationGateway/Program.cs` |
| Reader registered in opt-in (`CYGNET`) builds; the unsupported reader stays for other builds | `IntegrationGateway/Program.cs` |
| Status-flag mapping fix (see below) | `CygNetCurrentDataProvider.cs` |
| Server's three hard-coded ForeSite checks also accept `cygnet` (POC shortcut; see finding 5) | `ExecutionRepository.Reads.cs`, `ConfiguredLogicRepository.DirectSources.cs`, `ConfiguredLogicService.cs` |
| Sample module | `modules/poc/cygnet-well-input-check` |
| Probe used for all CygNet exploration | `poc/cygnet-probe` |
| Local AppHost DCP pin (machine-specific, drop it elsewhere) | `dev/.../WellSiteAutoPilot.AppHost.csproj` |

## Findings to turn into GitHub work
1. **The connection names the owning facility service (UIS), not FAC.** Facility records live in the FAC service associated with it. Resolve FAC with `ServiceInformation.GetAssociatedService(uis, ServiceType.FAC)` and query `facility_site = '<site>' AND facility_service = '<service>'`. Querying FAC directly returns 0 facilities. Fix the field help and examples in #311.
2. **Discovery needs paging and an honest completeness flag.** CYG_DEMO.UIS has 2063 facilities. The POC caps at 500 and still returns `complete=true`. A real reader should page and report truncation.
3. **Current values come from the realtime client on the UIS service.** `new RealtimeClient(uis).GetPointValueRecord(pointTag)` works. Using the PNT service fails with "is of type DBS, not CVS". (CYG_DEMO has no separately registered CVS; UIS serves realtime.)
4. **Quality mapping bug (#303).** Any non-zero status flag was mapped to Uncertain, but healthy points carry `Initialized, Updated` (and data-type bits such as `StringData`, `DigitalData`), so every read was rejected with `EXECUTION_INPUT_QUALITY_REJECTED`. Treat Initialized, Updated, StringData, DigitalData and OutputData as neutral; Unreliable is Bad; any other flag is Uncertain.
5. **Shared Server code hard-codes ForeSite's rules in three places.** The direct-source resource kind (`foresite.well`), the allowed quantities (`PumpFillage`, `AveragePumpFillage`) and `RequiredAdapterKey == "foresite"` run for every adapter, so CygNet was blocked by rules written for ForeSite. The POC only added `cygnet` and `cygnet.facility` beside those strings, which means every new adapter needs another edit in three places. The real fix: each adapter declares its own rules (CygNet: resource kind `cygnet.facility`, quantities from its configured point mapping; ForeSite: `foresite.well` and its own list) and the shared Server code asks the adapter. The CygNet-specific part then lives in the CygNet adapter project.
6. **Quantity to point mapping is Gateway-wide.** The POC maps `PumpFillage` to UDC `RRLPFILAVG` with `Providers__CygNet__Quantities__PumpFillage__Udc/Unit`. Per-connection or per-asset mapping is likely needed. Note that CygNet points are keyed by facility and UDC, and `providerMappingJson` is currently required to be empty.
7. **Domain is per connection, but the provider options carry a Gateway-wide domain.** `CygNetConnectionMeasurements` overrides it from the connection. #303's `data/current` route should follow.
8. **Freshness.** The demo data was about 15 minutes old, so `maximumAgeSeconds: 60` would reject it. The POC module uses 86400. Real modules need realistic ages.
9. **Gateway runs as one Windows account.** Every CygNet connection uses the Gateway's account and licence. Per-connection identity is not possible with the managed API as used.
10. **Opt-in build.** The Gateway switches to `net10.0-windows` and x64 only when `CYGNET_LIBRARY_PATH` is set, and the CygNet `*64.dll` files must be copied beside it. The ForeSite `Libraries\CygNet` folder works.
11. **A client is built per read** and the 10 s timeout leaves a thread running (the CygNet API is synchronous). Cache clients per domain/service.

## Environment facts
- Domain 22567 (WSANET, ARS 10.7.76.208): directory and CAL work, but service calls fail with DCL -21 because the service ports are blocked.
- Domain 27212 (CYG_DEMO): fully working from the dev box.
- Domain 9033 (SJVKRP, ARS 10.5.103.58): the workstation CAL is not enabled (DCL -30) and TCP 9033 is blocked.
- DCL errors: -11 service not found, -12 directory unreachable, -21 send to service failed, -30 CAL locked, disabled or expired.
- The managed `CygNet.API` assemblies load from .NET 10 with no COM registration. Needs `System.Configuration.ConfigurationManager` and an `AssemblyResolve` handler for the `*64` assemblies (already in the provider).

## How to reproduce
1. Set `CYGNET_LIBRARY_PATH` to the CygNet libraries folder and `Providers__CygNet__Quantities__PumpFillage__Udc=RRLPFILAVG`, `...__Unit=%`.
2. Start the AppHost (`dotnet run --project dev\WellSiteAutoPilot.AppHost -c Release --launch-profile Windows`).
3. `POST /api/v1/connections` `{name, adapterKey:"cygnet", settings:{domain:"27212", site:"CYG_DEMO", service:"UIS"}}`, then `/test`, `/discover`, `/import/preview`, `/import` (`kind: cygnet.facility`, `externalId: CYG_DEMO.UIS::RPOC_0001`).
4. Build and pack `modules/poc/cygnet-well-input-check` (`module validate`, `dotnet publish`, `module pack`), upload with `POST /api/v1/logic-modules/packages`, then approve.
5. `POST /api/v1/configured-logic` with `providerId: cygnet`, `providerAssetExternalId: CYG_DEMO.UIS::RPOC_0001`. Validate, activate (`If-Match: "none"`), then `/shadow`.
6. Probe: `dotnet poc/cygnet-probe/bin/.../Probe.dll <domain> <site>`, `facs <domain> <site> <service> [max]`, `pts <domain> <site> <service> <facilityId>`.

## Suggested work breakdown
1. Fix #311 wording (UIS owner, FAC association) and merge it.
2. Real facility reader with paging and truncation flag.
3. Adapter-declared source rules (resource kind, readable quantities) that the Server asks for instead of ForeSite string checks (finding 5), with tests.
4. Per-connection point mapping and the realtime read for saved connections (findings 3, 6, 7).
5. Fix the status-flag quality mapping in #303 with a unit test (finding 4).
6. Client caching and timeout handling (finding 11).
