# ForeSite adapter

The `foresite` adapter lets an engineer create a connection to a ForeSite installation. The adapter tests the connection and discovers wells with source-scoped queries.
See `foresite-discovery-contracts.md` for the POC evidence and
`../design/foresite-discovery-import-refresh-plan.md` for import and refresh planning.

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
An authenticated token handshake followed by `SurfaceNetworkService/Asset/All`. ForeSite has no health endpoint and `Administration/ServiceStatus`
takes about 16 seconds and reports on services AutoPilot does not need, so the cheap read that needs the same
permission discovery will is used instead.

| Answer | Outcome | Message says |
|---|---|---|
| 200 | Healthy | ForeSite answered and allowed this account |
| 401 / 403 | Needs attention | Reachable but did not accept the account; give it a ForeSite login |
| 404 | Needs attention | Not ForeSite's web services; check the address |
| other status | Needs attention | ForeSite answered with an error; wait if it has just started |
| refused / no route / 10 s timeout | Unreachable | Could not be reached (ForeSite can take about 90 seconds to be ready after it starts) |

A successful test must return a JSON array. ForeSite `FullErrorMessage` is shown when present, bounded to
1,024 characters by the adapter; raw response bodies and transport exception text are not displayed.

## Support position
ForeSite exposes no version endpoint, so compatibility is never claimed from the connection alone. The profile
the discovery slice was exercised against is ForeSite 5.7.1 (WellService). The live check is opt-in:
`FORESITE_BASE_URL=http://server dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests` as a Windows
identity ForeSite knows. The recorded checks always run in CI.

## Discovery

After the token handshake, read `Wells/UserAssets`, then post the returned asset IDs plus `0` to
`Wells/Well/Filter`. Post the identical scope to `WellConfig/GetWellAutonomousControlGroupData` and join
its device types by WellId. Never call `Wells/All` or submit an empty asset filter. ForeSite enforces
source access for the Gateway account, including whether it may read unmapped wells.

The same session cookie and Windows credentials cover every request. A 401 refreshes the token once and
replays the exact POST body; other failures are not retried. Required invalid inventories are incomplete;
optional device failures retain wells and emit a warning. Blank device types stay unknown. Existing
`PrincipalDeviceType` is used only as a fallback. An empty well list avoids a device lookup.

Discovery/import do not yet reconcile already-imported wells. The refresh plan separates new wells,
changed source facts/paths and inconclusive absence, with explicit review before changes to Assets.

## Current controller reads

The saved-connection Shadow path reads CurrentPumpFillage and AveragePumpFillage
through Surveillance. It uses LastGoodScanTime for successful datagroup acquisition,
with module freshness checked independently. ForeSite owns site-specific CygNet
UDC mapping; AutoPilot addresses ForeSite field names. See
[controller acquisition semantics](../implementation/foresite-controller-acquisition.md)
for the quality definition, exclusions and tests. Trends remain a historical-data
capability, not a replacement current-read path.
