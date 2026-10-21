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

## Discovery and device types

Discovery reads `Wells/All` using the Gateway's Windows identity and existing token/cookie session, then
`SurfaceNetworkService/Asset/All` for source asset names. Its well grouping metadata includes
`wellType` (lift method) and `deviceType` (controller/device classification); these are separate concepts.

ForeSite may supply device information in `PrincipalDeviceType` or `DeviceType`, either as a
simple value or a nested object with a `Name` and `Id`. The adapter reads the name for
`deviceType`, and preserves `deviceTypeId`, `deviceName`, and `deviceId` when present.
It does not infer a device type from the well's lift method.

For wells missing the device type in `Wells/All`, discovery uses the already-used
`Wells/Well/{id}` endpoint to fill the missing device metadata. The fallback is deliberately
limited to **25 wells per discovery** to prevent an N+1 read against large installations.
Failures, unsupported response shapes, and wells beyond this cap produce non-sensitive
warnings; the well inventory remains complete. The adapter never guesses a device type,
and bulk values win over conflicting detail values.

This uses recorded API shapes rather than a new unverified device-list endpoint. An opt-in
live validation against ForeSite is still needed to establish which fields the deployed
ForeSite version returns in practice.

## Support position
ForeSite exposes no version endpoint, so compatibility is never claimed from the connection alone. The profile
the discovery slice was exercised against is ForeSite 5.7.1 (WellService). The live check is opt-in:
`FORESITE_BASE_URL=http://server dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests` as a Windows
identity ForeSite knows. The recorded checks always run in CI.
