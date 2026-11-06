# POC handoff index: provider adapters

Start here. The builder has no access to ForeSite, CygNet or WAMI: use these docs and fixtures.

## Read in this order

1. `architecture/provider-adapter-architecture-recommendation.md`: layers, capabilities, provider differences, authoring, commands, PR order.
2. `implementation/provider-logic-module-gaps.md`: gap list G1-G26, ForeSite API findings, tracker of POC branches.
3. `implementation/foresite-adapter-reference.md`, `implementation/cygnet-adapter-reference.md`, `implementation/wami-adapter-reference.md`: per-provider routes, calls, units, live numbers, quirks.
4. `architecture/cygnet-wami-integration-research.md` and `architecture/provider-adapter-contracts.md`: earlier research and contracts.
5. `design/wami-provider-and-multi-provider-logic.md` (branch `feature/poc-wami-provider`): multi-provider composition design.

## Recorded fixtures

`implementation/fixtures/foresite/*.json`: real ForeSite responses (well 776 `RPOC_00016+9077` WellPilotRPOC, well 1596 AEBEAM): well status with `Units` block (1596), saved setpoints with units/ranges (776), available quantities, CygNet trends, KPI, setpoint data groups, alarms, daily averages, well test, optional quantities, controller capabilities, gauges. Build the ForeSite simulator (G10) from these. Anything with a person's name or credentials was not recorded; there are none.

WAMI and CygNet fixtures: `fixtures/cygnet/` (DDS device, data-group list, setpoint transaction XML, adapter catalog and history output) and `fixtures/wami/wami_live_results.json`.

## POC branches (origin)

Stacked in order; rebase each onto `origin/develop` and open a PR per branch, oldest first.

| Stack | Branches (in order) |
|---|---|
| ForeSite | G9, G2, G1, G12, G23, G3, G4, G7, G5, G10, G5b (`poc/gap-g05b-time-range-trends` is the tip) |
| CygNet | `poc/gap-cygnet-udc-catalog` -> `poc/gap-cygnet-history` -> `poc/gap-cygnet-alarms` |
| WAMI | `poc/wami-baseline` -> `poc/gap-wami-foresite-model-live` -> `poc/gap-wami-license-features` -> `poc/gap-wami-calc-functions` -> `poc/gap-wami-wellflo-model-library` -> `poc/gap-wami-gateway-registration` |

Each branch has offline tests (`dotnet run -c Release` in the matching `tests\WellSiteAutoPilot.*Tests`). Live clients need the opt-in paths (`CygNetLibraryPath`, `WamiInterfacesPath`); the Gateway and CI do not.

## Known pitfalls for the PR builder

- Central package management: add versions in `Directory.Packages.props` (System.ServiceModel.NetTcp 8.1.2). The POC pinned `Cryptography.Pkcs/Xml` 10.0.11, which carries advisories: use patched versions.
- Analyzer CA1050: types in a namespace or `internal`.
- `AdapterReadRules.CygNetQuantities` needs relaxing for `cygnet.udc.*`.
- WAMI `validate-model` is deliberately omitted (installed 7.2 contract lacks the result type).

## Not built / needs owner

Commands and recommendation/autonomous mode (architecture doc 10b); CygNet setpoint reader and element map (device type and the passive read are proven, see the CygNet reference); WAMI ESP/gas lift/PCP/plunger/rod-lift analysis functions; ForeSite RRL config to WAMI `RRLData` mapping (parked, see `ModelFileService.GetRRLData` in ForeSite); Gateway identity and `WCFAllowedGroups`; DLL redistribution; manifest V3 sign-off.
