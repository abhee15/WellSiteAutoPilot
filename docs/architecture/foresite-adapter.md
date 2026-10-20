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

## Support position
ForeSite exposes no version endpoint, so compatibility is never claimed from the connection alone. The profile
the discovery slice was exercised against is ForeSite 5.7.1 (WellService). The live check is opt-in:
`FORESITE_BASE_URL=http://server dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests` as a Windows
identity ForeSite knows. The recorded checks always run in CI.
