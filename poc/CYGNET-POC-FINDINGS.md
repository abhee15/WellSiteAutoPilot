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

## POC shortcuts and assumptions (nothing here is production-ready)
Every item below was a deliberate shortcut or an unverified assumption. Each needs a decision or real work in the GitHub build.

### Code shortcuts
1. **Quantity check skipped for CygNet.** In `ExecutionRepository.Reads.cs` the `PumpFillage`/`AveragePumpFillage` allow-list is bypassed when the adapter is `cygnet` (`!isCygNet && ...`). Any quantity name passes the Server; only the Gateway's configured map decides what can be read. The Server should validate against the adapter's declared quantities.
2. **`providerMappingJson` still must be `{}`** for CygNet. Point mapping is not per binding.
3. **Facility discovery cap of 500** returns `complete=true` (finding 2). Asset name is the bare facility id and only `facilityType` is stored as an attribute, so facility ids from different sites or services can collide and the description is dropped.
4. **`CheckAsync` only proves the FAC service can be resolved.** It does not run a facility query, so a connection can test Healthy yet fail at discovery (for example a wrong site/service pair that still resolves).
5. **Error text can reach the user.** `CygNetFacilityReader` puts `ex.Message` into `CygNetUnavailableException`. #311's contract is plain-words, redacted results. Use stable codes and keep CygNet text in the inner exception, as `CygNetCurrentDataProvider` does.
6. **No settings validation in the current-value route.** `CygNetConnectionMeasurements` reads `domain` only and does not run `AdapterSettingsValidator` or check connection id/revision as the ForeSite route does. It builds a provider per read; there is no client or session caching.
7. **Facility filter is built by string interpolation** (`facility_site = '<site>' ...`). It is safe today only because settings validation limits site/service to letters, digits, underscore and hyphen. Keep that validation or parameterize.
8. **`/adapters/cygnet/current` exists only in `CYGNET` builds.** A normal build has no route, so Server reads for CygNet connections would fail there with a not-found, which the Server reports as a generic gateway failure.
9. **Assembly resolver registered twice** (facility reader and provider static constructors). Harmless, but should be one registration.
10. **#303 status mapping:** flags treated as neutral (Initialized, Updated, StringData, DigitalData, OutputData) are my judgement. A point carrying `ConfigurableBit1` (for example `RRLPFILCUR`) still maps to Uncertain, and the meaning of the configurable bits is site-specific and unconfirmed. No unit test was added.
11. **No tests were added** for the reader, the route, the Server changes or the quality mapping.

### Module and data assumptions
12. **Semantic mismatch.** The module asks for current `PumpFillage`, but the POC maps it to the UDC `RRLPFILAVG` (an average). Which UDC is correct for "current pump fillage" (`RRLPFILCUR` vs `RRLPFILAVG`) was not decided; I chose the one that was not flagged.
13. **The module is a copy of the ForeSite input check** with a new id. Version 1.0.0 (max age 60 s) failed on quality and stays installed in the POC database; 1.0.1 (max age 86400 s) is the one that ran. The 86400 s is only to accommodate stale demo data.
14. **Gateway-wide quantity map via environment variables** (`Providers__CygNet__Quantities__PumpFillage__Udc/Unit`). Missing or wrong entries are not surfaced in the UI.
15. **Demo-domain data is not representative.** Some points on `RPOC_0001` carry 2023 timestamps and others are about 15 minutes old. Behaviour on a live producing site (update rates, quality flags, point naming) is unknown.
16. **UDC names and the facility/point model are specific to the demo site's RPOC configuration** (`RRL*` UDCs). Other sites may differ.

### What was and was not tested
- Tested: one domain (27212), one site, one service (UIS); connection test; discovery; import of 3 wells; one configured logic; one Shadow run, run by the bootstrap administrator account.
- Not tested: the web UI, scoped grants and permissions for CygNet assets, Live mode, scheduled runs, source checks and source changes for CygNet connections (`SourceChanges.cs` is ForeSite-specific), connection revision changes, disabling a connection, more than one concurrent read, Python modules, a non-`CYGNET` build under real use (it compiles, see below), and the persistence/application test suites (they need `WSA_DATABASE_CONNECTION_STRING`, which I did not set up).
- Build/test checks done: Gateway builds with and without `CYGNET_LIBRARY_PATH`; `WellSiteAutoPilot.CygNetAdapterTests` passes. The CygNet-specific projects are not in `WellSiteAutoPilot.slnx`, so CI never compiles the reader or the route.
- Carried over from the #311 review and still open: the catalog lists the CygNet type even on builds that cannot use it; the CI script relaxes the "baseUrl first field" assertion; all connections share the Gateway's Windows account and licence.

### Environment and local-only state (not in the branch)
- Library source: `D:\P4\WFT_Services\ForeSiteService\Trunk\Libraries\CygNet` (ForeSite's CygNet libraries, v18.0.0.0). Licensing of redistributing them with the Gateway was not checked.
- Dev box, Windows account `WFT\E206862`, ARS registry entries under `HKLM\SOFTWARE\Visual Systems, Inc.\CygNet\Domains\<id>`. The domain 27212 CAL happened to be enabled for this workstation.
- The AppHost csproj carries a local DCP 13.5.1 pin and `appsettings.Local.json` was copied from another worktree (database `wsa_cardspoc`, NATS under `D:\AppData\wsa-cardspoc`). Neither belongs in the real change.
- The generated web client (`src/web/src/api/generated/v1`) is untracked and was not committed.
- POC data (the CygNet connection, 3 imported wells, two configured logics and modules 1.0.0/1.0.1) lives only in the `wsa_cardspoc` database.
