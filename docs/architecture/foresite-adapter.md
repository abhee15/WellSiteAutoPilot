# ForeSite adapter

The `foresite` adapter lets an engineer create a connection to a ForeSite installation. This slice covers
describing and testing the connection. Reading wells (discovery and import) arrives with delivery step 4 in
`docs/design/connections-workflow-design.md`.

## Setting
| Setting | Meaning |
|---|---|
| ForeSite address (`baseUrl`) | Where ForeSite's web services answer, for example `http://foresite-server`. Required. |

## Identity
ForeSite uses Windows authentication. The Integration Gateway calls it as **its own Windows account**, so that
account needs a ForeSite login and the assets it should see (ForeSite scopes reads by the caller's assigned
assets). Choose the account at install time (`installer/README.md`, "The Integration Gateway's account"); a
group-managed service account is recommended. Connection settings never select the account, and the test result
states which account was used.

## What "Test" does
One read of `SurfaceNetworkService/Asset/All`. ForeSite has no health endpoint and `Administration/ServiceStatus`
takes about 16 seconds and reports on services AutoPilot does not need, so the cheap read that needs the same
permission discovery will is used instead.

| Answer | Outcome | Message says |
|---|---|---|
| 200 | Healthy | ForeSite answered and allowed this account |
| 401 / 403 | Needs attention | Reachable but did not accept the account; give it a ForeSite login |
| 404 | Needs attention | Not ForeSite's web services; check the address |
| other status | Needs attention | ForeSite answered with an error; wait if it has just started |
| refused / no route / 10 s timeout | Unreachable | Could not be reached (ForeSite can take about 90 seconds to be ready after it starts) |

Nothing ForeSite sends back is ever shown or stored.

## Scoped well discovery and control-group device types

The authoritative POC contract is in [ForeSite discovery contracts](foresite-discovery-contracts.md).
The `poc/foresite-asset-discovery` branch investigated ForeSite source
(`IWellService`, `IWellConfigurationService`, `WellService.AddAssetFilter`) and
recorded a live test on a system with 1,403 wells. ForeSite has **three different
service routes** for the workflow:

1. `GET Wells/UserAssets` — `AssetDTO[]`, the assets the Gateway Windows account
   may discover. This includes the `Id = 0` "Not Mapped" pseudo-asset **only when the
   account is authorized**. `SurfaceNetworkService/Asset/All` remains the connection's
   health probe but must not be used as the discovery scope; it does not list 0.
2. `POST Wells/Well/Filter` — one JSON `WellFilterDTO` body:
   `{"AssetIds":[28,0]}`. ForeSite applies its own access intersection. `AssetId=null`
   denotes unmapped wells (asset 0). A missing/empty filter could mean *all* wells,
   so never issue the POST when `UserAssets` is empty.
3. `POST WellConfig/GetWellAutonomousControlGroupData` — use the **same filter**,
   read the `Values` rows (`WellId`, `DeviceType`), and join with the scoped wells
   on `WellId`. A null/blank device type means unknown. This endpoint does **not**
   return a controller ID or additional device details.

All requests reuse the Gateway's Windows-authenticated `HttpClient`, session cookie
from `TokenService/CreateToken` and one 401 refresh/retry. Discovery never calls
`Wells/All` or `Wells/GetWellsByUserAssetIds` (which excludes unmapped wells).

The adapter also checks every returned well's mapped `AssetId` against the authorized
UserAssets scope, rather than relying exclusively on a service-side filter. Unknown
IDs or unapproved unmapped wells invalidate the discovery (`Complete=false`). A
failed scoped well request is incomplete; a failed optional control-group request
produces a warning and retains the scoped well inventory. `wellType` (lift type)
remains separate from `deviceType`. Blank classifications are never guessed.

ForeSite exposes no pagination for these calls; avoid per-well HTTP requests. Known
remaining performance risk: cold-cache control-group reads on a system of roughly
16,500 wells, where ForeSite may consult CygNet per well.

### Validation boundaries

- **CI contracts:** recorded, sanitized DTO fixtures validate the asset-ID POST
  filters, unmapped access, device join, out-of-scope rejection, malformed/error
  responses, authentication and cookie refresh.
- **CI simulator integration:** ForeSite stand-in through Gateway and Server
  exercises full discovery/preview/import without reaching any on-prem server.
- **On-prem smoke check:** optional ForeSite test, run manually or using an internal
  self-hosted Windows runner with the intended service account and network access.
  This validates the real server's latency, permissions and contracts, which cloud
  GitHub Actions cannot verify. Do not put Windows credentials or production
  responses into CI fixtures.

## Support position
ForeSite exposes no version endpoint, so compatibility is never claimed from the connection alone. The profile
the discovery slice was exercised against is ForeSite 5.7.1 (WellService). The live check is opt-in:
`FORESITE_BASE_URL=http://server dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests` as a Windows
identity ForeSite knows. The recorded checks always run in CI.
