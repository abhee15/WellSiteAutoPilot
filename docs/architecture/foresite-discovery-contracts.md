# ForeSite discovery: API contracts

How the ForeSite adapter finds wells and their device types. Checked against the ForeSite source
(`IWellService`, `IWellConfigurationService`, `WellService.AddAssetFilter`) and tested against a ForeSite test server
(1,403 wells: 292 mapped to 37 assets, 1,111 not mapped). Paths are relative to the services root (port 80).
Every call needs Windows authentication plus the cookie from `GET TokenService/CreateToken`.

## Flow

1. `GET Wells/UserAssets` lists the assets this user may see.
2. `POST Wells/Well/Filter` with those exact user asset ids (including `0` only when returned) retrieves the wells and their details.
3. `POST WellConfig/GetWellAutonomousControlGroupData` with the same body returns each well's device type.

Wells are joined on well id. The asset list only supplies names; the other two calls carry everything else.
If step 1 fails, discovery is incomplete. If step 3 fails, discovery completes with a warning and no device types.

## 1. User assets

`GET Wells/UserAssets` returns `AssetDTO[]`.

```json
[
  {"ChangeUser":null,"ChangeDate":"2019-01-18T23:13:57.157Z","Id":4,"Name":"Asset1","Description":"Asset1"},
  {"ChangeUser":null,"ChangeDate":null,"Id":0,"Name":"Not Mapped","Description":"Not Mapped"}
]
```

- Id `0` is the "Not Mapped" pseudo-asset, listed only when the user may see wells that belong to no asset.
- `SurfaceNetworkService/Asset/All` never lists id `0`, so it is used for the health check, not for discovery.

## 2. Wells for a set of assets

`POST Wells/Well/Filter`, body is a `WellFilterDTO`:

```json
{"AssetIds":[28,0]}
```

- Several ids go in one array. `0` or `null` selects wells with no asset. `{}` or an empty array selects every well the user may see.
- ForeSite intersects the ids with the user's own assets, so ids the user lacks are dropped without an error. Id `0` counts only if the user may see unmapped wells (`ALL_USERS_CAN_ACCESS_UNMAPPED_WELLS` or an explicit grant).
- Property names are case-insensitive. There is no pagination or batching: one call returns the whole result.

The response is a `WellDTO[]` of about 70 fields. The ones the adapter reads:

```json
{"Id":70,"Name":"32G 17-14P","AssetId":28,"WellType":10,"FacilityId":"RPOC_0070","Field":"name","Lease":"lease","PrincipalDeviceType":null}
{"Id":593,"Name":"10A07","AssetId":null,"WellType":10,"FacilityId":null,"PrincipalDeviceType":null}
```

- `AssetId` is the well's stored asset and is `null` for unmapped wells.
- `WellType` is a numeric `WellTypeId`. It stays separate from device type.
- `FacilityId` can be null.
- `PrincipalDeviceType` is never filled by list calls, so device type comes from step 3.

Observed on the test server: `[0]` gave 1,111 wells, `[28]` gave 3, `[28,0]` gave 1,114, and all user assets plus `0` gave 1,403.

### Calls not to use for discovery

- `Wells/All` applies no user or asset filtering.
- `Wells/GetWellsByUserAssetIds` (POST, body `[28,5]`) works for mapped assets but returns nothing for id `0`.

## 3. Device type

`POST WellConfig/GetWellAutonomousControlGroupData`, body is the same `WellFilterDTO`:

```json
{"AssetIds":[28,0]}
```

The response is `{"Units":null,"Values":[...]}`, one row per well:

```json
{"WellId":973,"WellName":"18_5","WellType":2,"DeviceType":"LufkinSam","LogicEnabled":true,"LogicMode":2,
 "ActiveLogicModule":{"ControlId":-1,"ControlText":"None"},"ActiveLogicModuleId":-1,"ASULogics":null}
```

- It takes a filter, not a well id or control group id. One call covers many wells.
- It honours the asset filter: `{"AssetIds":[28]}` returned 3 rows and `{}` returned all 1,403.
- `DeviceType` is the only device field. There is no device id or controller detail, and no asset, field, lease or facility.
- "Control group" here is ForeSite's automated setpoint update (autonomous control) configuration. A well has one row and at most one active logic module, so there is no list of control groups to reconcile.
- The server reads `DeviceType` from its cached well status, or from CygNet when the cache is empty, so it is only filled for CygNet-sourced wells. Blank and null both mean "not known".

Device types seen on the test server: WFordKS 81, WellPilotRPOC 38, LufkinSam 6, AEBEAM 6, WFTA1K 3, EProd8800 1,
EmersonFB107 1, SAMIWC 1. The other 1,266 wells are null or blank.

## Mapping to discovery facts

| Fact | Source |
|---|---|
| `assetName` | `Name` of the well's asset from step 1. Wells with no asset take the name of asset `0` ("Not Mapped"). |
| `wellType` | `WellType` id from step 2, shown as its abbreviation. |
| `deviceType` | `DeviceType` from step 3 (falls back to `PrincipalDeviceType` if present). |
| `field`, `lease`, `facilityId` | Step 2. |

## Not yet verified

- A server with about 16,500 wells: the cold-cache cost of step 3, which may call CygNet per well.
- A user with restricted assets, or one who may not see unmapped wells.
