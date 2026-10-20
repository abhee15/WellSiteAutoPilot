# POC findings: PR #205 connections, providers, adapters, asset discovery

Branch `poc/connections-205` (from `develop`). Local POC against the ForeSite `POP` database (10 wells). Not production-ready.

## What was built
| Area | Change |
|---|---|
| ForeSite adapter | `src/integration/WellSiteAutoPilot.Integration.ForeSite`: read-only discovery + health. Tests in `tests/WellSiteAutoPilot.ForeSiteAdapterTests` (recorded + live via `FORESITE_BASE_URL`). |
| Gateway | `Connections/Connections.cs`: `AdapterRegistry`, JSON-file `ConnectionStore` (revisions, stale-write 409), endpoints `/adapters`, `/connections` CRUD, `/test`, `/discover`. Windows (Negotiate) auth on these endpoints. |
| Server | `Connections/ConnectionEndpoints.cs`: admin-only (`SecurityManage`) proxy under `/api/v1/connections`, `GET /templates`, `POST /{id}/discover/apply` (template-based, idempotent). |
| Web | `ConnectionsPage.tsx` under Administration > Connections: adapters, connection CRUD, Test, Discover, template selector + hierarchy preview, Apply. |

## Findings and decisions
1. **Adapter registration**: explicit-code factory registry (one adapter instance per connection); no reflection or plugin scanning.
2. **Health**: ForeSite has no health endpoint. Any server response counts: `GET SurfaceNetworkService/Asset/All` 200 = Healthy, 401/403 = Degraded (reachable, no permission), connect error or timeout = Unavailable. `Administration/ServiceStatus` is not used (about 16 s, dependents are not required).
3. **Mapping fields** (adapter metadata): `assetId, assetName, wellId, wellName, wellTypeId, wellType, deviceType, field, lease, facilityId`.
4. **WellType** is an enum on the DTO; the adapter maps id to name (1 RRL, 2 ESP, 3 GInj, 4 GLift, 5 NF, 6 WInj, 7 WGInj, 8 PLift, 9 OT, 10 PCP, 12 SInj, 13 CInj).
5. **Device type** exists only for wells with a data connection (empty for all POP wells); a missing value is shown as "Unspecified device". Hence templates are user-selectable.
6. **Hierarchy templates** (levels from `asset, wellType, deviceType`, always ending in `well`):
   - `asset-type-device-well`: Asset > WellType > DeviceType > Well
   - `asset-type-well`: Asset > WellType > Well
   - Custom: any ordered, distinct subset, including just `well` (flat list).
   - Fallback labels: "Unassigned" (no ForeSite asset), "Unknown type", "Unspecified device". Groups are asset type `foresite-group`; wells `foresite-well`. Apply is idempotent per connection + external id.
7. **Paging**: no ForeSite service API takes paging or a next-page token, so none is needed for ForeSite. Keep the contract able to carry one for other providers.
8. **Auth**: Gateway uses Windows auth (like the Server). ForeSite and CygNet use Windows auth, so the service account needs ForeSite permissions and assigned assets (reads are scoped by `WellFilterDTO`).

## Verified live
Simulator Healthy; ForeSite discovered 10 wells; Apply created group hierarchy (second Apply created 0); stale revision gives 409; anonymous Gateway call gives 401.

## Gaps for the real build
- Persist Connection as a DB entity with migration (currently a JSON file; the Server needs a shared store).
- Existing Gateway `/integrations` and `/data/current` are not yet under auth (the worker calls them anonymously).
- Credentials/secret handling and service-account identity are undecided. CygNet attribution under Windows auth is unclear.
- Localization: UI strings are inline English.
- Group name uniqueness (global vs per parent) is unverified; the code retries with a disambiguated name on conflict.
- ForeSite takes about 90 s after start to warm its well cache before `Wells/All` returns 200.
- The generated TS client is not committed (repo convention); regenerate with `dev/scripts/generate-api-client.mjs`.

## CygNet adapter (second POC slice)

Modelled on ForeSite's well-configuration **Facility picker**, which calls `DataConnection/AvailableFacilitiesWithStatus/{domain}/{site}/{service}` and ForeSite's `CygNetFacilityHelper.GetWellFacilities`:
1. `ServiceInformation.GetAssociatedService(domain/site/service, ServiceType.FAC)` resolves the FAC service.
2. `Facilities.Client.GetFacilityTagList("facility_site = '..' AND facility_type = 'WELL' AND facility_service = '..'")` lists tags (the well type name is a ForeSite system setting; default `WELL`).
3. `ReadFacility(tag)` gives `Type` and `Description`. A `SecurityException` on one facility means component-level security hides it; ForeSite reports this as a "readFailed" flag, not an error.
4. The picker shows `FacilityId | Type | Description`.

What was built:
| Area | Change |
|---|---|
| Adapter | `src/integration/WellSiteAutoPilot.Integration.CygNet`: `CygNetFacilityDiscoveryProvider` (discovery, health, metadata), `CygNetScope` (domain, site, service, facilityType). External id is `site.service::facilityId`. No CygNet assemblies are loaded. |
| Agent | `src/integration/WellSiteAutoPilot.CygNetAgent`: small ASP.NET Core process, Negotiate auth + impersonation, `GET /health`, `GET /facilities?domain&site&service&type`. NOT in the solution; built on a CygNet machine with `-p:CygNetLibPath=<CygNet Bin>`. |
| Gateway/Server/UI | Connections now carry `Settings`; adapters declare `SettingKeys` (the UI renders the fields). New `cygnet` adapter; new built-in template `cygnet-site-service-facility`; template levels `site`, `service`, `facilityType` added. |
| Tests | `tests/WellSiteAutoPilot.CygNetAdapterTests` (recorded; live with `CYGNET_AGENT_URL`, `CYGNET_DOMAIN`, `CYGNET_SITE`, `CYGNET_SERVICE`). |

Findings:
- **The agent can be net10, not .NET Framework.** ForeSite's own net10 host (`CygNetWrapper.Net10.csproj`) references the same `CygNet.API.*` assemblies, so `cygnet-wami-integration-research.md` section 1a (net48 agent) can be relaxed. The agent project compiled against the 9.9 assemblies. Whether the native `Cx*64.dll` pieces load in a given install still needs the owner's box.
- **Health:** any answer from the agent is Healthy; agent up but CygNet unreachable (503) is Degraded; agent down is Unavailable. Same philosophy as ForeSite.
- **Not proven live:** this box has no CygNet install (`C:\CygNet` holds only ODBC support). The agent ran here and returned 401 for anonymous callers and 503 for CygNet calls, as designed. The facility read itself was verified only against a recorded-shape fake agent (3 facilities discovered; template Apply created Site > Service > Facility, idempotent). Run the live check on a CygNet machine.
- Domain, site and service are typed in by the user. The agent could later expose `GET /services` (ForeSite's `GetFACServicesByDomain`, `GetCurrentValueServicesForDomain`) so the UI can offer pickers.
- Other facility kinds: the agent takes `type`, so pads, tanks or meters are the same call with a different `facilityType` (use `parentFacilityId` + facility attribute for children, as `GetFacilities` does).
- Gap: the Server still stores imported assets as `foresite-well`/`foresite-group` types; a provider-neutral asset type is needed.
- Gap: the `readFailed` (partial permission) flag is dropped by the discovery contract; add a warnings channel.

## Running the POC
See `connections-205-run.md`.
