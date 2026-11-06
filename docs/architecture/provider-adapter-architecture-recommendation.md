# Provider adapter architecture: recommendation for WellSite AutoPilot

Audience: the engineer (or agent) building the adapters **without access to ForeSite, CygNet or WAMI**. Everything needed is in this doc, the three reference docs and the recorded fixtures listed in `poc-handoff-index.md`.

Status: recommendation from the POC work (ForeSite, CygNet, WAMI), all verified live from the dev box unless marked *(unverified)*.

## 1. Principles

1. **A module asks for intent; an adapter serves it.** Modules never see a provider API, credentials or connection details.
2. **Discover, do not declare.** The adapter reports what exists for a connection and asset (key, kind, unit, range, applicability).
3. **Units, quality and timestamps always come from the provider.** Never force a unit or a range. Never use retrieval time as the data time.
4. **Fail closed, with precise codes.** No recommendation from partial or ambiguous results.
5. **Never assert cross-adapter equivalence.** ForeSite and CygNet values matched in the test lab only because the test ForeSite is fed by that CygNet system.
6. **Bounded freedom.** Every read is limited to catalog entries, bounded windows and counts, and a server-side allowlist.
7. **Proprietary binaries stay out of the main build.** Use a client factory seam, opt-in project and simulator.

## 2. Layers (same shape for every provider)

| Layer | Purpose | ForeSite | CygNet | WAMI |
|---|---|---|---|---|
| Contracts (`Integration.Contracts`) | Neutral capability interfaces, `AdapterDescriptor`, `EngineeringValue` | shared | shared | shared |
| Adapter project | Maps contract calls to a small provider client interface; classifies errors | `Integration.ForeSite.Adapter` | `Integration.CygNet.Adapter` (`ICygNetClient`, `CygNetCatalog`, `CygNetHistoryReads`) | `Integration.Wami` (`IWamiClient`, `WamiContracts`, `MultiProviderCoordinator`) |
| Live client | Real transport. Opt-in when it needs proprietary DLLs | HTTP + Windows auth | `Integration.CygNet` (`CygNetApiClient`, needs `CygNetLibraryPath`) | `Integration.Wami.NetTcp` (needs `WamiInterfacesPath`) |
| Simulator / fake | Offline, deterministic, fixture-driven | to build from fixtures | `SimulatedCygNetClient` | `FakeWamiClient` |
| Gateway registration | `AdapterCatalog` descriptor + `AdapterConnectionTester` branch | existing | existing | added (`WamiAdapter.Descriptor`, `IWamiClientFactory`) |

Rule: the adapter project has **no** reference to a proprietary assembly. Only the live client does, via an MSBuild property, and the Gateway selects it through a factory registered at runtime. A build without the DLL reports "no WAMI client in this build" in the connection test rather than failing.

## 3. Capability kinds

`AssetDiscovery`, `CurrentData`, `HistoricalData`, `Alarms`, `Setpoints`, `Catalog`, `Calculation`, `ModelArtifact`, `Command`, `Verification`.

| Kind | ForeSite | CygNet | WAMI |
|---|---|---|---|
| AssetDiscovery | yes | facilities | no |
| CurrentData | status doc, one read per execution | point values (batch per facility) | no |
| Catalog | `AvailableQuantities` + `Units` + optional/calculated quantities | PNT point list + point records | function descriptors |
| HistoricalData | `POST CygNetTrends`, `DailyAverage` | VHS | no |
| Alarms | `CurrentAlarms`, intelligent alarms | CAS `GetAlarmsForPoints` | no |
| Setpoints | `AllSavedSetPointParams` (self-describing) | raw DDS data-group transaction per device type *(not built)* | no |
| Calculation | no | no | `nodal-analysis`, `ipr-flow-rate`, `pvt-properties`, `well-performance-curve` |
| ModelArtifact | `ModelFiles/CurrentWellModel` (text config for RRL) | no | `WellFloModelLibrary` (`.wfl` files) |
| Command | design review (G8) | design review | no |

An adapter advertises only what it implements. A module requiring a missing capability fails **validation**, not the run.

## 4. Catalog and keys

- Entry: `key`, `displayName`, `kind` (`current|daily|history|welltest|setpoint|alarm`), `unit`, `min`, `max`, `applicability` (well type, device type, availability).
- Core keys are portable (`PumpFillage`, `AverageSPM`, `CyclesYesterday`). Provider keys are namespaced: `foresite.optional.<name>`, `cygnet.udc.<UDC>`.
- Applicability is intersection of: provider availability (ForeSite `AvailableQuantities`, CygNet "the point exists"), well type, device type. Device type is **not** well type.
- Catalog is cached per `(connection revision, asset)` with a TTL; invalidated on connection revision change.
- Sentinels: `65535` on a count field means *unset*: report `MISSING`, keep the raw value in the result. A real `0` is a value (legacy logics got this wrong).

## 5. When each kind of data is fetched, and storage

| Data | When | Stored by us? |
|---|---|---|
| Catalog | asset import / connection test, and on demand with a TTL | cache only |
| Well type, device type, time zone | asset import, as asset attributes | yes: attributes, no new tables for ForeSite |
| Current values | each execution, one provider read fanned out to all inputs | pinned in the execution record |
| History / daily | each execution, bounded window | pinned summary only |
| Setpoints, alarms | each execution | pinned |
| Models | each execution, hashed (SHA-256) and pinned | no |

**Decision:** no extra tables for the ForeSite adapter. ForeSite APIs are the source. The only additions are asset attributes (`wellType`, `deviceType`, `timeZone`).

Time: day boundaries use the **asset's** time zone, not the server's (G25). Daily snapshot reads (`*Yesterday`) are rollovers at gauge-off time, so read the yesterday field rather than computing it from history.

## 6. Read rules (server-side)

`AdapterReadRules` holds allowlists. They must be provider-neutral: ForeSite keys are table-driven, CygNet keys accept `cygnet.udc.*` (**still to relax**; today `CygNetQuantities` is a fixed list), bounded window length, bounded entry count (CygNet history 50,000), response-size and depth bounds on HTTP.

## 7. Failure and outcome taxonomy

Per call: `NotStarted` (safe to retry with backoff), `Failed`, `Ambiguous` (dispatched, then timeout or cancel; never retried automatically; flagged for operator review).

| Area | Codes |
|---|---|
| Composition | `DEPENDENCY_SET_INVALID / ADAPTER_MISMATCH / ASSET_MISMATCH / PIN_MISMATCH / DENIED / UNAVAILABLE` |
| Inputs | `INPUT_MISSING / QUALITY / UNIT / STALE / UNAVAILABLE` |
| Models | `MODEL_NOT_FOUND`, `MODEL_PIN_MISMATCH`, `MODEL_FORMAT_UNSUPPORTED`, `MODEL_REJECTED` |
| WAMI | `WAMI_UNAVAILABLE / BUSY / TIMEOUT / LICENSE_MISSING / CALCULATION_FAILED`, `CALCULATION_CANCELLED` |
| CygNet | `CYGNET_QUANTITY_UNSUPPORTED`, DCL errors (`-12` no directory service, `-30` CAL) |
| ForeSite | `ForeSiteMeasurementException` codes; `401` can mean wrong verb or route as well as auth |

## 8. Multi-provider composition

A module declares **roles**, not adapters: `data` (ForeSite or CygNet), `model` (ArtifactSource), `calc` (WAMI, named functions). Manifest V3 `providerDependencies: [{role, kind, adapterKey, functions?}]`; V1/V2 `RequiredAdapterKey` stays valid; V3 may not also set it. Validator: 1-8 roles, unique case-insensitive, `Functions` only on Calculation.

Each role binds to `{connectionId, connectionRevision, logicalAssetId, externalId}`. Identity is **never** inferred from equal external ids across connections. The coordinator (`MultiProviderCoordinator`): authorise each role, read inputs (quality, unit, age gates), fetch the model and hash it, call WAMI, build `EngineeringValue`s with provenance `wami:{conn}@{rev}:{function}:{sha12}`, emit one audit event per step. Model format must be `wami-model-file` for WAMI, otherwise `MODEL_FORMAT_UNSUPPORTED` (ForeSite RRL models are `foresite-rrl-config`, text, and need a mapping to `RRLData` that is **parked**).

Execution option A (pre-acquire inputs, model and calculation, then run the pure module) is built. Option B (in-module governed broker) is later.

## 9. Identity and credentials

- ForeSite: Windows identity -> `GET TokenService/CreateToken` -> session cookie.
- CygNet: the Gateway process's Windows identity (CygNet must know who the user is, see `cygnet-wami-integration-research.md`).
- WAMI: Windows transport security on net.tcp; identity must be in `WCFAllowedGroups` (default `Users, Administrators`). In Edge mode the router refuses non-local clients. **Open:** which identity the shipped Gateway runs as.
- No secrets in manifests or modules. Connection settings hold hosts/ids only.

## 10. Testing without providers

1. Adapter tests run against the simulators/fakes (`dotnet run -c Release` in `tests\WellSiteAutoPilot.{ForeSiteAdapterTests,CygNetAdapterTests,WamiProviderTests}`).
2. Fixtures in `docs/implementation/fixtures/` are recorded live responses. Build the ForeSite simulator (G10) from them. CygNet and WAMI shapes are in the reference docs.
3. Live clients compile only with the opt-in paths; CI builds the offline projects only.
4. Pin the `Cryptography.Pkcs/Xml` package versions to patched ones (the POC used 10.0.11, which NuGet flags).

## 10a. How the providers differ, and what that means for authoring

| Question | ForeSite | CygNet | WAMI |
|---|---|---|---|
| What is it? | Well-centred application layer (calculated status, setpoint groups, alarms, ACL logic) | Raw point/SCADA layer (facility + UDC points, history, alarms, device transactions) | Engineering calculation server (WellFlo models) |
| Unit of addressing | Well id | `SITE.SVC:FACILITY_UDC` | Session + model |
| Quantity set | Fixed enum (~75 `WellQuantity`), availability per well | Whatever points exist on the facility | n/a (functions) |
| Applicability source | `AvailableQuantities` + well/device type | the point exists | licence + model contents |
| Derived values (yesterday, averages, inferred production) | Computed already in the status doc | Plain UDC points written by the controller/host | computed on request |
| History | Daily averages; sub-daily via `POST CygNetTrends` (about 5 min) | Native VHS, 30 min entries seen, any bounded window | none |
| Setpoints | Typed, self-describing groups (unit, range, read-only, value, original) | Raw DDS data-group transactions; needs per-device-type map | none (inputs are model data) |
| Alarms | Typed current + intelligent alarms | CAS alarms plus `RRLALM*` digital points | none |
| Writes | ForeSite setpoint and recommendation APIs (section 10b) | UIS commands / DDS tokenised transactions (section 10b) | none; mutates only the session model |
| State | Stateless HTTP | Stateless, synchronous API | Stateful session, one worker per session, max 10, no queue |
| Failure feel | 401 for wrong verb or route too | DCL `-12/-30` for domain/directory problems | busy fault, licence, COM hang |

Authoring consequences:

1. Modules use **core keys** (`PumpFillage`, `AverageSPM`, `CyclesYesterday`, `IdleTimeYesterday`...) and the host resolves them per adapter. Provider-namespaced keys make a module non-portable: allowed, but flagged by `module validate`.
2. A module states required kind, key, window and freshness, for example "daily `InferredProductionYesterday`, last 2 days, max age 36 h". `validate` checks against the catalog for the bound asset.
3. Setpoints are logical names (`MinFillageTarget`, `MaxSpeed`, `IdleTime`), mapped per controller family. A module never writes ForeSite element ids or CygNet data-group names.
4. Quantities that exist on only one provider are capability-flagged. Prefer reading the provider's precomputed value over recomputing it from history.
5. The same module, run against the ForeSite and CygNet simulators on the shared fixture set, must give the same result. Do not use live values to claim equivalence.
6. A module's output is a typed **proposal** (setpoint changes with unit, reason, evidence). What happens to it (shadow, recommend, autonomous) is host policy, not module code.

## 10b. Commands: recommendation and autonomous mode

Not built; this section is the design input. Nothing here should be implemented before the governance design is reviewed.

**Provider write paths found**

| | ForeSite (preferred first path) | CygNet |
|---|---|---|
| Write setpoints | `POST Surveillance/SendSetPointsParams/{waitForResult}` (a `SetPointsDTO` group), `SendAndGetMultipleSetPointParamsForSingleWell(SetPointsDTO[], waitForResult)` (send, return readback), `SaveAndGetMultipleSetPointParamsForSingleWell` (save only, no device send) | DDS `GetTokenizedDataGroupTransaction(dgType, ordinal, time, uisCommandParam, relation, timeout)` through the UIS command; per-device-type map gives group name, get/send command names, hidden elements, partial-write flag, ordinal |
| Read back | `GetSavedSetPointsParamsForSingleWell(wellId, dgType, ordinal, scanOption)`, `AllSavedSetPointParams`. `scanOption` triggers a live device read through a UIS command (failed on stale wells) | `GetLatestSetpointTransaction` (passive) or a get command to refresh |
| Device commands | `Wells/Command/{command}`, `Well/Command={command}`, `ExecuteNamedUisCommand(commandInfo, name, params)` | UIS command by name |
| Recommendations | `ApplyRecommendationForEachSetpoint(ASUWorkbenchParameterDTO)`, ASU workbench | none native |
| Autonomous task | `RunAutonomousControlTask(taskId)`, `GetACLLogicSettings`, `GetRRLAutonomousControlHistory` (401 on GET: verify verb and route first) | none |
| Pair semantics | A `SetPointsDTO` carries multiple elements together (covers G20 paired up/down speeds) | partial-write flag per group |

**Modes**

1. **Shadow:** module runs and its proposals are stored, never sent.
2. **Recommend:** proposals go to a person for approval (ForeSite can hold them as ASU workbench recommendations). Approval sends via the command seam.
3. **Autonomous:** approved policy sends automatically within limits.

**Design requirements**

- One neutral `ICommandProvider.ProposeAsync/ApplyAsync`: input is logical setpoint changes (name, value, unit, expected current value), output is outcome plus readback. The adapter owns the controller-family map.
- Pre-checks: the asset's current value equals the module's expected value (detect an operator change: "do not fight the operator", G18); value within the provider's min/max and not read-only; group not partially writable if a partial write is not allowed; connection write grant on that asset.
- Limits owned by the host, not the module: max step, floor and ceiling, rate limit per asset per day, allowed hours, kill switch.
- Outcome taxonomy reuses `NotStarted / Failed / Ambiguous`. A timed-out device write is **Ambiguous**, never retried blindly; read back first.
- **Verification is mandatory:** after a write, read back (`Verification` capability) and compare with the requested value within tolerance. Record requested, sent, readback, and who/what approved.
- Revert: record the original value (ForeSite returns `OriginalValue`) so a "back out" proposal is possible. A long closed loop is expressed as a scheduled follow-up run, not a sleeping module (G17).
- Audit every proposal and write: module + version, inputs pinned, mode, approver, provider, connection revision, outcome.
- Credentials and the write path live in the Gateway; modules never hold them. Writes need their own grant, separate from read.
- Simulator parity: the ForeSite and CygNet simulators must support write, readback and failure injection (timeout, device reject, stale) before any live use.

**Order:** read-only setpoints (ForeSite, then CygNet) -> shadow mode -> verification readback on the simulator -> ForeSite recommend/approve -> ForeSite autonomous in a test well -> CygNet writes after the device-type map exists. Writes on the shared test ForeSite must wait for the owner's go-ahead.

## 11. Recommended PR order

1. Contracts: catalog entry, units/range, setpoint read, series result, history/alarm shapes.
2. ForeSite: data-driven status field map (G1/G9/G23/G12), units from provider (G2), sentinel handling.
3. ForeSite catalog (G3), simulator from fixtures (G10), then setpoints (G4/G11/G19/G24/G26), history (G5/G22), alarms (G7/G14).
4. CygNet catalog (PNT), history (VHS), alarms (CAS), relax `CygNetQuantities`; then device type and setpoints.
5. WAMI: Wami project + fake + coordinator, NetTcp client + factory, functions, model library, Gateway registration.
6. Manifest V3 + role binding persistence; snapshot/host accept V3; wire coordinator behind a flag.
7. Writes (G8/G20/G21) last, after design review.

## 12. Owner decisions still open

Function priority for WAMI; DLL redistribution; Gateway Windows identity and `WCFAllowedGroups`; WAMI server version string; ESP/gas lift/PCP/plunger/rod-lift `PerformAnalysis` (untested); manifest V3 sign-off; write governance.
