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

## Asset-scoped discovery contract (pending ForeSite API verification)

**Do not use `Wells/All` for discovery.** The ForeSite global route may return wells
that the Gateway Windows account is not authorized to discover through its assigned assets.
Filtering a global response locally is not a substitute for a provider-enforced scoped read.

The intended sequence, using a single authenticated Gateway account and the existing
`TokenService/CreateToken` session cookie throughout, is:

1. **Accessible scope:** `GET SurfaceNetworkService/Asset/All`. Interpret its successful JSON
   response as the accessible assets for that Windows account. Preserve asset ID and display name.
   ForeSite's special asset ID **0** denotes wells with no mapped asset; include it in the next
   stage **only when the returned accessible-asset scope explicitly contains 0**. Do not infer
   access to unmapped wells or silently add 0.
2. **Asset-filtered wells:** Pass the *entire distinct accessible asset ID list* to ForeSite's
   well-details/filter operation. The actual path, HTTP method, payload shape, response schema,
   and behavior for an empty or 0-only asset list **have not been established by the available
   ForeSite contract evidence**. Verify those details on an authorized ForeSite instance or from
   its API service source before wiring it up. Never substitute `Wells/All`.
3. **Control-group device type:** For the wells returned by that scoped operation, read their
   control-group data to obtain the device/controller type and associated identifiers. The exact
   endpoint, key association (well ID vs another identifier), batch support, and response fields
   **are also unverified**. Keep `wellType` (well lift category, e.g. RRL or ESP)
   separate from `deviceType` (from the control group). Do not infer device type from WellType.
4. **Results:** Map only authorized, returned wells into `DiscoveredAsset` with their
   accessible ForeSite asset membership, asset name, well name and ID, field, lease, lift type,
   and control-group device type. Check that each returned `AssetId` is among the accessible
   IDs, including 0 only where authorized; unexpected IDs must never enter discovery.

**Failure semantics:** Any failed/partial well-scoped read or missing required control-group
information must be reported with a warning and `Complete=false` (so downstream reconciliation
cannot mark wells as missing based on incomplete data). Avoid leaking server response bodies.
If ForeSite returns zero accessible assets, a successful asset-scope read permits a complete
empty discovery. Optional classification details may be omitted with warnings only when the
application explicitly allows an "Unspecified device" classification; never silently invent
them. For large installations, respect server paging/batch limits, avoid per-well N+1 calls,
and preserve the existing Server preview/import limits.

**Current branch checkpoint:** As of this draft PR, `DiscoverAsync` validates the asset
scope using the token-cookie session, never calls the unscoped well endpoint, and returns
`Complete=false` if accessible assets exist because stages 2 and 3 are not yet verified.
This is deliberate fail-closed behavior, **not a finished discovery implementation**.
The draft must not be merged until stages 2 and 3 and their scope tests are implemented.

Acceptance checks before making this PR mergeable:
- Recorded/mock scope test with multiple assets + 0, and a 0-only case; prove the exact
  asset-ID list reaches the filtered well operation and no global well endpoint is called.
- Unauthorized/failed asset-scope, scoped-well, and control-group calls do not produce
  "complete" results or disclose unapproved wells; validate each returned well's AssetId.
- Control-group records correctly map the device type and ID, without confusing well type;
  missing/duplicate/mismatched control groups are handled safely.
- HTTP auth and cookie revalidation; bounded/paged reads where supported; final Gateway,
  import-preview, import, Shadow E2E and installation CI checks pass for the exact PR head.
- A live authorized integration test against ForeSite confirms the endpoint names, request DTO,
  and control-group response contract; record sanitized fixtures for CI.

## Support position
ForeSite exposes no version endpoint, so compatibility is never claimed from the connection alone. The profile
the discovery slice was exercised against is ForeSite 5.7.1 (WellService). The live check is opt-in:
`FORESITE_BASE_URL=http://server dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests` as a Windows
identity ForeSite knows. The recorded checks always run in CI.
