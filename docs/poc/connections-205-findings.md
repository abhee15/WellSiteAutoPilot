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

## Running the POC
See `connections-205-run.md`.
