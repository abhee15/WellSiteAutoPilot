# WAMI as an internal dependency provider, and multi-provider Logic Modules

Status: proposal plus proof of concept (branch `feature/poc-wami-provider`). Nothing here changes live routing,
`RequiredAdapterKey` behaviour or the main manifest schema. Facts marked *(unverified)* come from source reading only;
no live ForeSite or WAMI server was available.

## 1. Scenario

A ForeSite-well configured logic (1) reads real-time data per ForeSite's current adapter capabilities, (2) fetches the
model file applicable to that asset through ForeSite, (3) sends the model plus inputs to a WAMI server to run a
calculation (well analysis), (4) evaluates a module on the result. WAMI is first an internal calculation-service
dependency; later it might become an external adapter exposing selected functions.

## 2. What WAMI offers (from research)

| Topic | Finding |
|---|---|
| Protocol | WCF `net.tcp`, router at `net.tcp://<host>:5656/Weatherford/IRouter/<Contract>` (contracts `IModel`, `IReciprocatingRodLift`, `IESP`, `IPCP`, `IGasLift`, `IPlungerLift`, `ILicense`, `IConfiguration`, `IModelFile`). |
| Auth / binding | `SecurityMode.Transport`, Windows credentials, very large message limits, 6 min send/receive timeouts (as ForeSite's `WAMIBinding`). The caller's Windows identity must be accepted by WAMI. |
| Contract | Proprietary `Weatherford.WAMI.Interfaces.dll` (.NET Framework 4.0, v7.7.x). Not redistributed here; referenced only through an opt-in MSBuild property. ForeSite itself loads it on net10 through `System.ServiceModel.NetTcp` `ChannelFactory`. |
| Statefulness | Session per channel: `OpenFileData(FileData{File=bytes})` returns `FileStatus`, then operations act on that opened model. Workers are COM-based processes, so calls may be slow, busy or hang. |
| Functions | IModel: `DoNodalAnalysis`, `...WithOption`, `...WithModelData`, `GenerateWellPerformanceCurve`, `CalculatePVTData`, `CalculateFlowAssurance`, `ValidateModel`, `Get/SetModelData`, `SetOperatingPointTopNode`. Rod lift: `PerformAnalysis`, `PerformCalibration`, `PerformDesign`, `BalancePumpingUnit`, pattern match. ESP: `DoESPNodalAnalysis`, pump wear calibration. ILicense: `HasWAMILicense`, diagnostics. |
| Results | e.g. `NodalAnalysisResultData`: `OperatingPoint` (liquid/oil/water/gas rate, bottomhole pressure, ...), `ResultStatus` (Success/Failure/Exception/ValidationErrors), `ModelValidationResult`. Units unknown (ForeSite has a UnitLibrary). |
| Side effects | `SetModelData`, `SetOperatingPointTopNode`, calibration mutate the *session's* model, not ForeSite's stored one. Declared per function, never assumed. |
| Model files via ForeSite | `IModelFileService` (webHttp JSON, `http://host/ModelFiles`): `GET CurrentWellModel/{wellId}` returns `ModelFileDTO` {Id, ApplicableDate, WellId, WellType, IsProsper, base64 `Contents`, ...}; also `Model/{modelId}`, `ModelConfig/{wellId}`. "Current" is the newest `ApplicableDate <= now` for the well's current well type *(unverified live)*. |

## 3. Architecture

### 3.1 Capability and descriptor model
Existing `AdapterDescriptor` already carries capabilities. WAMI is an adapter with key `wami` and a `Calculation`
capability and no discovery (`WamiAdapter`). A provider additionally publishes **function descriptors**
(`WamiFunctionDescriptor`: name, inputs, outputs, `Effect` = None/ChangesSessionModel). Model files are a separate
**artifact capability** (`IModelArtifactSource`), because the inline scalar/card envelope cannot carry files.

### 3.2 Named provider dependencies per module
A module declares *roles*, not adapters: `data:foresite`, `model:foresite`, `calc:wami`. Each role = capability kind +
adapter key (+ optional required function names). Roles are bound independently, so data and model may come from
different ForeSite connections; **identity is never inferred from equal external ids across connections** (the POC
test uses id 597 on two connections to prove this).

Manifest: keep `RequiredAdapterKey` valid for V1/V2 (single role `data:<key>`). A manifest V3 adds
`dependencies: [{role, kind, adapterKey, functions?}]`; the validator rejects duplicate roles, unknown kinds and a
V3 manifest that also sets `RequiredAdapterKey`. Do not replace the key with an unvalidated list.

### 3.3 Configured-logic binding
Each role binds to `{connectionId, connectionRevision, logicalAssetId, externalId}`. Binding checks: connection enabled and
adapter matches the role; logical asset matches across roles; each role has its own grant (authority below).

### 3.4 Read plan and pinning
The execution pins, per role: connection id and revision; for data, source binding and per-input quality/unit/age; for
model, `ModelArtifactRef` (id, applicable date) **and SHA-256 of the bytes** computed locally; for calc, connection
revision, function and result provenance `wami:{conn}@{rev}:{function}:{sha12}`. A pinned hash mismatching the fetched
model fails `MODEL_PIN_MISMATCH` rather than running a different model. This extends the existing
`PinReadSourcesAsync` (connection, source binding, revision) with artifact pins.

### 3.5 Governed calculation-call contract
- **Timeout** per call via linked token and `TimeProvider`; **cancellation** propagates and aborts the channel.
- **Outcome classification** (`WamiCallOutcome`): `NotStarted`, `Failed`, `Ambiguous`.
- **Retry** only for `Busy`/`NotStarted`, bounded. After dispatch, a timeout or cancel is *ambiguous* (a COM worker may still be running) and is never retried automatically; the execution records `Ambiguous`.
- **Side effects**: functions declared `ChangesSessionModel` run only on a session opened for that execution; the session is always disposed, and model files are never written back to ForeSite.
- Results become `EngineeringValue`s with provenance; non-success statuses and validation errors are failures, not values.

Two execution options: **A (implemented)** pre-acquisition: the host resolves inputs, model and calculation, then runs the
pure module. **B (later)** governed in-module broker: the module requests intermediate calls through a host
interface with an allow-list, per-execution call budget and the same contract. A is enough for the scenario and keeps modules free of I/O and credentials.

### 3.4b Authority and permissions
Authorization is per role (`IDependencyAuthorizer`): read authority on `data`, artifact-read on `model`, execute
authority on `calc`. Modules never see credentials or call providers. Denied roles fail `DEPENDENCY_DENIED` before any
I/O on that role. Open: which Windows identity calls WAMI (service account vs impersonation).

### 3.5b Failure modes (codes in the POC)
`DEPENDENCY_SET_INVALID / ADAPTER_MISMATCH / ASSET_MISMATCH / PIN_MISMATCH / DENIED / UNAVAILABLE`,
`INPUT_MISSING / QUALITY / UNIT / STALE / UNAVAILABLE`, `MODEL_NOT_FOUND / MODEL_PIN_MISMATCH / MODEL_*`,
`WAMI_UNAVAILABLE / BUSY / TIMEOUT / LICENSE_MISSING / MODEL_REJECTED / CALCULATION_FAILED`, `CALCULATION_CANCELLED`.
Each is fail-closed: no recommendation is produced from partial results.

### 3.6 Audit
One event per role step (`CompositionAuditEvent`): role, adapter, connection+revision, outcome, model hash, function,
duration, call outcome. Ambiguous completions are flagged for operator review.

### 3.7 Later: WAMI as an external adapter
Expose a curated subset of function descriptors through the adapter contract (calculation capability, typed inputs and
outputs with units). The internal provider and external adapter share `IWamiClient`; only descriptor publication and
per-function authorization are new.

## 4. Single-provider assumptions to change (not changed here)
- `ConfiguredLogicService.cs` ~427-448 and `ConfiguredLogicRepository.DirectSources.cs` ~18-30: one connection per adapter key, direct sources only "foresite" with one target.
- `ConfiguredLogicRepository.GrantLifecycle.cs` ~119-122 and `LogicModuleManifestValidator.cs` ~34: `RequiredAdapterKey`.
- `ICalculationProvider.CalculateAsync(string, Dictionary<string,decimal>)`: no model or session; too thin for WAMI.

## 5. Phased plan (small PRs)
1. **This PR**: design, contracts, fake, ForeSite model source, coordinator, offline tests, opt-in net.tcp client.
2. Live ForeSite check: confirm `ModelFiles/CurrentWellModel/{id}` route, auth and size on a test server (read-only).
3. Live WAMI smoke on a Windows dev box: licence check, `OpenFileData`, `DoNodalAnalysis`; settle units and the input mapping (`SetOperatingPointTopNode`).
4. Manifest V3 `dependencies` + validator; V1/V2 unchanged.
5. Role bindings in configured logic (schema, repository, UI), one role = one connection.
6. Artifact and calc pins in execution reads; audit events persisted.
7. Wire coordinator into the executor behind a feature flag; Gateway hosted on Windows for net.tcp auth.
8. Optional: governed in-module broker (option B); WAMI as external adapter.

## 6. Risks
- Proprietary DLL: redistribution rights unconfirmed; keep it out of the repo and the main solution.
- Windows-only, Windows-auth net.tcp; Linux hosts cannot call WAMI directly.
- Worker busy/hang and double-execution after timeout; WAMI queue behaviour unknown.
- Units of results and inputs unknown; wrong units silently corrupt recommendations.
- Model size and transfer time (8 MiB cap in POC); stale "current" model selection.
- Licence expiry mid-operation; transitive WCF crypto packages carry open advisories (the opt-in project downgrades NU1902/3 to warnings).

## 7. Open questions for the owner
1. Units and node-id/input mapping for live inputs (`SetOperatingPointTopNode`)?
2. WAMI behaviour when workers are busy: queue, fault or timeout? Is a calculation idempotent?
3. Which functions to expose first (nodal analysis, rod-lift analysis, ESP)?
4. Is the ForeSite `ModelFiles` route reachable by the AutoPilot service identity?
5. May `Weatherford.WAMI.Interfaces.dll` be redistributed, or must builds take it from an install?
6. Which Windows identity calls WAMI?
7. Manifest V3 shape acceptable (roles with kind + adapter key)?

## 8. What the POC proves
Offline (fake WAMI, stubbed ForeSite HTTP): multi-connection role binding, pins and hash checks, per-role authority,
input quality/unit/staleness gates, busy retry, licence/down/timeout/cancel/ambiguity semantics, and a module producing
a recommendation from WAMI output. Run `dotnet run --project tests\WellSiteAutoPilot.WamiProviderTests`.
Not proven: live ForeSite route, any live WAMI call, units, input mapping. The net.tcp client in
`src\integration\WellSiteAutoPilot.Integration.Wami.NetTcp` compiles against the real contract
(`-p:WamiInterfacesPath=...`) but has not been run against a server.
