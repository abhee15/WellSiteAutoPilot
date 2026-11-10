# Provider gaps found by live review

These gaps were found while reviewing provider-touching changes against the live ForeSite, CygNet and WAMI systems available on the laptop. The GitHub-side agent has no access to those systems, so each item is evidence from a real run. Evidence is quoted with the PR and well or facility used.

Status: OPEN means not yet fixed on `develop`. INFO means a verified finding with no action needed. Priority is a first proposal (P1 correctness or trust, P2 robustness or consistency, P3 documentation or tooling) for the team to adjust.

Before working an item, re-check it against `origin/develop`, because later PRs may have changed it.

## Summary

| ID | PR | Provider | Gap | Priority | Status |
|----|----|----------|-----|----------|--------|
| G1 | #488 | CygNet | History and current quality is `Uncertain` for every sample | P1 | OPEN |
| G9 | #491 | ForeSite | Unknown well returns 200 with an empty body, so `FORESITE_CONFIGURATION_NOT_FOUND` never fires | P1 | OPEN |
| G15 | #531 | ForeSite | Applicable quantities for an unknown well returns an empty set, not an error | P1 | OPEN |
| G16 | #544 | ForeSite | Unknown well handling differs between the snapshot read and the applicable quantities read | P1 | OPEN |
| G14 | #519 | ForeSite | Numeric `AvailableQuantities` IDs are not mapped to quantity names | P1 | OPEN |
| G7 | #485 | ForeSite | History supports only `PumpFillage`; other lift types fail | P2 | OPEN, by design, needs documentation |
| G2 | #488 | CygNet | Historian client cache is never evicted or disposed on failure | P2 | OPEN |
| G5 | #488 | CygNet | Double and Int64 value paths untested live | P2 | OPEN |
| G6 | #488 | CygNet | DST boundary, denied access, timeout and dense range beyond the cap untested | P2 | OPEN |
| G10 | #491 | ForeSite | `PrincipalDeviceType` is null on both test wells; `ClassificationCode` is a raw number | P2 | OPEN |
| G11 | #491 | CygNet | CygNet connection data provider and capability reference not exercised live in that review | P2 | OPEN |
| G21 | CygNet | CygNet | The simulated, live or unconfigured backend is one Gateway-wide setting, so connections cannot mix | P1 | OPEN |
| G22 | CygNet | CygNet | A live CygNet Gateway is an opt-in separate build with no packaging or deployment path | P1 | OPEN |
| G23 | CygNet | CygNet | Every CygNet connection uses the Gateway account, and each domain must be registered on the Gateway host | P1 | OPEN |
| G24 | CygNet | CygNet | Gateway-wide domain, quantity map and `data/current` route still sit beside the per-connection path | P2 | OPEN |
| G25 | CygNet | CygNet | The simulated client serves the same facilities for every connection | P2 | OPEN |
| G17 | Adapters page | ForeSite, CygNet | The capability reference lists no error codes or refusal reasons | P2 | OPEN |
| G18 | Adapters page | ForeSite | The reference omits the shipped snapshot read and applicable quantities reader | P2 | OPEN |
| G19 | Adapters page | CygNet | CygNet operation names, keys and units are inconsistent with ForeSite | P2 | OPEN |
| G20 | Adapters page | PetroVisor, Simulator | Two adapters are selectable but have no capabilities and no explanation | P3 | OPEN |
| G13 | #519 | ForeSite, CygNet | Only ForeSite surveillance has a workflow guide | P3 | OPEN |
| G3 | #488 | CygNet | `CygNetHistoryProbe` fails to build with CA1826 | P3 | OPEN |
| G4 | #488 | CygNet | Assembly version reported as 18.0.0.0 against file version 9.9.1000 | P3 | OPEN |
| G8 | #490 | Tests | `SecurityPersistenceTests` not run; needs a scratch database | P3 | OPEN |
| G12 | #519 | ForeSite | Workflow guide verified live | n/a | INFO |

## Details

### G1 CygNet quality is Uncertain for every sample (#488)
Live `BaseStatus` is `Initialized, Updated, ConfigurableBit1`. `ConfigurableBit1` is not in `CygNetValues.NeutralFlags`, so every history sample, and the current-value path, reports `Uncertain`. Decide what `ConfigurableBit1` to `ConfigurableBit3` mean on a site, or allow a per-connection neutral flag list.

### G2 CygNet historian client cache (#488)
The cache is never evicted or disposed after a failure. A dead client is possible after a VHS restart. Untested.

### G3 CygNet probe build (#488)
`tools/WellSiteAutoPilot.CygNetHistoryProbe` fails to build with CA1826 at `Program.cs` line 36.

### G4 CygNet assembly version (#488)
The reported assembly version is 18.0.0.0 while the file version is 9.9.1000. Evidence must record the real version.

### G5 CygNet value types (#488)
Live values arrive as UTF-8 text. The Double and Int64 value paths are untested live.

### G6 CygNet untested conditions (#488)
Not tested: DST boundary, denied access, timeout, and a dense range beyond the cap. UTC timestamps were confirmed live (`Kind=Utc`).

### G7 ForeSite history scope (#485)
History supports only `PumpFillage`, a rod pump quantity. Well 1596 (AEBEAM) returns `FORESITE_TREND_FAILED`. Quality is always `Unknown`. Limits are 31 days and 1000 samples. This is by design and needs to be documented for authors.

### G8 Security persistence tests (#490)
`SecurityPersistenceTests` need `WSA_DATABASE_CONNECTION_STRING` and a scratch database, so they were not run.

### G9 ForeSite unknown well (#491)
For an unknown well (999999) ForeSite returns HTTP 200 with an empty body, not 404. The provider reports `FORESITE_CONFIGURATION_INVALID`, so `FORESITE_CONFIGURATION_NOT_FOUND` never fires live. Treat 200 with an empty body as not found.

### G10 ForeSite device type (#491)
`PrincipalDeviceType` is null on wells 776 and 1596, so the device type mapping is untested with a real value. `ClassificationCode` is the raw well type number (`"1"`), with no name.

### G11 CygNet connection provider (#491)
The new `CygNetConnectionDataProvider` and its capability reference were not exercised live in that review. Only the ForeSite configuration read was.

### G12 Workflow guide verified live (#519, INFO)
On wells 776 and 1596 the `AvailableQuantities` and well routes exist and `LastGoodScanTime` is present. Well 1596 returns null fillage and a shorter applicable list (15 IDs against 32), so field presence alone is not proof of applicability. The precheck is addressed by #531.

### G13 Workflow guide coverage (#519)
The running app returns the `applicable-surveillance` workflow (3 steps, reference only) for ForeSite surveillance only. ForeSite well configuration, well trends and cards, and the CygNet surveillance group, have no guide. Authors get no guidance on the history limits (31 days, 1000 samples) or on unknown well behaviour (G9).

### G14 Quantity ID mapping (#519, #531)
The guide tells authors to resolve `AvailableQuantities` IDs with the provider `WellQuantity` enum, but the numeric ID to quantity name mapping is not verified. Live IDs: well 776 returns 1 to 24, 28, 30 to 32, 34 and 69 to 71 (32 IDs); well 1596 returns 1 to 10, 13, 14 and 17 to 19 (15 IDs). The guide's UI rendering was not checked visually.

### G15 Applicable quantities for an unknown well (#531)
`ForeSiteApplicableQuantities.ReadAsync` returns an empty set (count 0) for unknown well 999999, so a caller cannot tell an unknown well from a well with no applicable quantities. Valid wells match the raw probe: 776 returns 32 IDs and 1596 returns 15. Invalid IDs (`abc`, `0`) fail with `FORESITE_WELL_ID_INVALID`. The reader is not yet wired into module reads. The 16 KiB cap and malformed payloads are covered only by offline tests.

### G16 Snapshot read and unknown wells (#544)
Verified live: on well 776 `PumpFillage` is 99.6 and `AveragePumpFillage` is 99.67, with one acquisition time and one source, and the scalar read is unchanged. Well 1596 fails closed with `FORESITE_MEASUREMENT_MISSING`. A duplicate quantity gives `FORESITE_SNAPSHOT_QUANTITY_DUPLICATE` and an unknown quantity `FORESITE_QUANTITY_UNSUPPORTED`. Unknown well 999999 gives `FORESITE_READ_FAILED` here, while the applicable quantities read returns an empty set for the same well (G15). The two readers should agree.

### G17 No error codes in the reference (Adapters page)
Checked against `/api/v1/connections/adapter-operations` on the running app. No operation carries an error or refusal list, yet live reads return distinct codes: `FORESITE_MEASUREMENT_MISSING` (well 1596 has null fillage), `FORESITE_WELL_ID_INVALID`, `FORESITE_QUANTITY_UNSUPPORTED`, `FORESITE_SNAPSHOT_QUANTITY_DUPLICATE`, `FORESITE_READ_FAILED` and `FORESITE_TREND_FAILED` (G7). Authors cannot learn what to handle from the page. Add a per operation error list to the catalog metadata and render it.

### G18 Shipped ForeSite reads missing from the reference (Adapters page)
Every ForeSite current operation declares `maximumQuantities` 1 and the catalog has no entry for the shared snapshot read (#544, several quantities, one acquisition time) or the applicable quantities reader (#531). Both were verified live. Well 776 has 32 applicable IDs but the page names only `PumpFillage` and `AveragePumpFillage`, with nothing saying which quantities a given well supports (G14).

### G19 CygNet naming and units differ from ForeSite (Adapters page)
CygNet keys have no separator (`current.pumpfillage`, `current.motoramps`) while ForeSite uses `current.pump-fillage`. CygNet display names are raw quantity names (`CasingPressure`), ForeSite uses readable ones (`Pump fillage`), and ForeSite history reads `PumpFillage history`. Every CygNet operation has an empty `unit`, and its example shows `site-unit` and `timestampKind` null, while ForeSite shows `%` and a timestamp kind. CygNet has one capability group (`Surveillance data`) and no history or configuration entry. Agree one naming and unit rule in the catalog.

### G20 Adapters without capabilities (Adapters page)
PetroVisor and Provider Simulator return zero capabilities and zero operations. They are still selectable, so the page shows an empty list with no reason. Either hide them, or say that no reference is published.

## CygNet: several systems on a deployed Gateway
Reviewed against `develop` (`bf24e458`). The user need: configure several CygNet systems as connections, as is done for ForeSite, on a deployed system.

### What already works per connection
A connection carries `domain`, `siteService`, `facilityFilter` and `pointMap`. Asset ids are `SITE.SERVICE::FACILITY`, bindings are per connection, the facility and realtime client caches are keyed `domain/site.service`, and the call gate allows 4 concurrent calls per service. So two connections to different domains or services do not collide in the adapter. The gaps below are around it.

### G21 Backend is Gateway-wide
`Providers:CygNet:Backend` (`unconfigured`, `simulated` or `live`) picks one `ICygNetClient` for the whole Gateway (`Program.cs`). Every CygNet connection, test and discovery uses it. The default is `unconfigured`, which refuses every call with a Gateway configuration message, and the local dev AppHost sets none. ForeSite has no such switch: its simulator is a separate adapter key. Decide between: (a) a per-connection `mode` setting (Live or Simulated), with simulated allowed only when the Gateway enables it; (b) a separate `cygnet-simulated` adapter key, matching the Provider Simulator pattern. Recommended: (b) for production safety, backed by a client router that chooses by adapter key, so a simulated connection can never read real data and the reverse. The descriptor has no choice-type setting today (`AdapterSettingKind`), so (a) also needs a new setting kind.

### G22 Live build and deployment
`live` is compiled only when `CYGNET_LIBRARY_PATH` is set (`#if CYGNET`), the Gateway then targets `net10.0-windows`, and the project is outside `WellSiteAutoPilot.slnx`. CI, the installer and the normal build never produce it, so a deployed Gateway cannot be CygNet-capable unless someone builds it by hand on a CygNet host. Needed: a defined build and packaging step (a second Gateway artifact or a CygNet sidecar service), how the licensed libraries reach it, and a startup check that reports "live backend not built in" clearly.

### G23 Identity and domain registration
All connections read as the Gateway's Windows account, which needs a CygNet licence and access in every domain. There is no per-connection credential. The CygNet client also finds a domain through the host's own CygNet configuration; on the dev laptop domains had to be registered in the registry for the probes to resolve them. So a new CygNet system needs both a connection and host-level domain setup, and the connection test cannot say which is missing. Needed: a documented per-system onboarding checklist, a test message that separates "domain unknown to this host" from "account refused", and a decision on whether per-connection credentials are in scope.

### G24 Legacy Gateway-wide settings
`Providers:CygNet:Domain` (ambient domain when absent), the `Quantities` map and the `CygNetCurrentDataProvider` on the `data/current` route predate connections. `CygNetApiClient.ReadCurrentAsync` still builds a provider per read from the connection domain, over a Gateway-wide default map of demo rod-lift UDCs (`RRLPFILAVG` and others) that is silently wrong on other systems. `pointMap` overrides are free text capped at 400 characters, with no UI help. Needed: make the per-connection map the only path for saved connections, keep the legacy route for development only, and show the effective map in the test result.

### G25 Simulated client is one fixed system
`SimulatedCygNetClient` serves the same eight facilities for any domain, site and service, so two simulated connections look identical apart from the tag prefix. That is good for tests but cannot rehearse two systems with different wells, point maps or stale data. Needed: simulated profiles chosen per connection, for example by site name, and a second fixture system.

### Proposed order
1. Decide G21 (adapter key or mode) and G23 (credentials in or out of scope).
2. Build the client router and per-connection backend choice, with tests for two connections on different backends.
3. Define the live Gateway build, packaging and startup check (G22).
4. Add per-connection simulated profiles (G25) and move the point map fully per connection (G24).
5. Live test on this laptop with two real domains once step 2 is in.

## Suggested order
1. Agree one unknown well rule for ForeSite (G9, G15, G16).
2. Decide the CygNet quality mapping (G1).
3. Verify and record the quantity ID mapping (G14), then wire the applicability check into reads.
4. Add workflow guides for the other capability groups and document the history scope (G13, G7).
5. Adapters page catalog fixes: error lists, missing reads, CygNet naming and units, empty adapters (G17 to G20).
6. CygNet robustness and live tests (G2, G5, G6), then tooling items (G3, G4, G8, G10, G11).
