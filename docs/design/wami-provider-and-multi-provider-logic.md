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
| Protocol | WCF `net.tcp`, router at `net.tcp://<host>:5656/Weatherford/IRouter/<Contract>` (contracts `IModel`, `IReciprocatingRodLift`, `IESP`, `IPCP`, `IGasLift`, `IPlungerLift`, `ILicense`, `IConfiguration`, `IModelFile`). Primary source: WellFlo trunk `API\Server` (`Weatherford.WAMI.Server`, hosted with CoreWCF on net10 or WCF on net472) and `Utilities\WamiClient` (reference client, `App.config`). |
| Server internals | `Router` is one untyped passthrough service (`PerSession`): per session it takes a worker process from `RouterEndpointManager.GetWorker` and forwards each message over net.tcp (Windows transport) to that worker, so **one client session = one dedicated COM-based worker** (state lives there). Port, `MinFreeWorkers` (5), `MaxAllowedWorkers` (10, also capped by the licence's max-concurrent-connections), `WCFAllowedGroups` (`Users, Administrators`), `FlowAssuranceTimeoutMillis` (25 s) come from the server `App.config`/`ServerOverrides.config`. Workers that fault or exceed ~350 MB are recycled. |
| Busy behaviour | **No queue**: when `MaxAllowedWorkers` workers are in use, `GetWorker` throws `FaultException<WorkerLimitFault>{Message, MaxAllowedWorkers}` before anything runs. The client maps it to `Busy` / `NotStarted`, which is safe to retry with backoff. |
| Hosting restriction | In **Edge mode** (ForeSite Edge node-lock licence) the router refuses non-local clients, so WAMI must then run on the same machine as the Gateway. |
| Auth / binding | `SecurityMode.Transport`, Windows credentials, very large message limits, 6 min timeouts (server `Router`, ForeSite `WAMIBinding` and the WellFlo `WamiClient` `App.config` agree); the sample client sets an empty `servicePrincipalName` identity so SPN matching is skipped. The caller's Windows identity must belong to a group in `WCFAllowedGroups`. |
| Contract | `Weatherford.WAMI.Interfaces` (source in WellFlo trunk `API\Common\WellFlo.API.Interfaces`; ForeSite carries a .NET Framework 4.0 build v7.7.x). Redistribution of the binary is unconfirmed, so it is referenced only through an opt-in MSBuild property. `IModel : IConfiguration`, `IModelFile : ILicense`; the client uses `IModel` and `ILicense` endpoints. |
| Model files | `IModelFile.OpenFileData(FileData{Name, Extension, File=bytes})` returns `FileStatus`; `Open(path)` reads a server-local path (not used: the model comes from ForeSite). `GetFileData()` returns the in-session model; `GetModelFileVersion()` and `GetApplicationVersion()` give versions. |
| Statefulness | Session per channel: open the model, then operations act on it. Workers are COM-based processes, so calls may be slow or hang. |
| Functions | IModel: `DoNodalAnalysis`, `...WithOption`, `...FromModelData`, `GenerateWellPerformanceCurve`, `CalculatePVTData`, `CalculateFlowAssurance`, `ValidateModel`, `Get/SetModelData`, `SetOperatingPointTopNode(nodeID, pressure)` (`-1` = keep the model's value). Rod lift: `PerformAnalysis`, `PerformCalibration`, `PerformDesign`, `BalancePumpingUnit`, pattern match. ESP: `DoESPNodalAnalysis`, pump wear calibration. ILicense: `HasWAMILicense`, `IsLicenseAvailable`, `GetCurrentLicense`, diagnostics. |
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
A module declares *roles*, not adapters: role `data` (kind Data, adapter foresite), role `model` (kind Model, foresite), role `calc` (kind Calculation, wami, functions `nodal-analysis`). Each role = capability kind +
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
Done on this branch: (a) design, contracts, fake, ForeSite model source, coordinator, offline tests; (b) opt-in net.tcp client and live probe (PR 3 below, proven); (c) manifest V3 prototype (PR 4, validate-only); (d) sample module `Integration.Wami.Sample`.

1. Live ForeSite check: confirm `ModelFiles/CurrentWellModel/{id}` route, auth and size on a test server (read-only). **Still open.**
2. Live WAMI smoke: done (section 9).
3. **Smallest first PR (prototyped here)**: manifest V3 `providerDependencies` (role id + kind + adapter key + optional functions), validator (1-8 roles, unique case-insensitive, identifiers, `Functions` only on Calculation, no `RequiredAdapterKey` with V3, dependencies rejected on V1/V2) and the pure `ProviderRoleBindingValidator` (unknown/duplicate/unbound role, missing/disabled connection, adapter mismatch). V3 is validated but fail-closed downstream: snapshot, compatibility and worker hosts still accept only V1/V2, so a V3 module cannot be installed or run yet. V1/V2 behaviour is unchanged (tests in `LogicManifestTests`).
4. Persist role bindings in configured logic (schema, repository, UI); one role = one connection; link each data requirement to a role.
5. Snapshot/compatibility/host accept V3; artifact and calc pins in execution reads; audit events persisted.
6. Wire the coordinator into the executor behind a feature flag; Gateway hosted on Windows for net.tcp auth.
7. Optional: governed in-module broker; WAMI as external adapter.
## 6. Risks
- Proprietary DLL: redistribution rights unconfirmed; keep it out of the repo and the main solution.
- Windows-only, Windows-auth net.tcp; Linux hosts cannot call WAMI directly.
- Worker busy: no queue, hard fault at `MaxAllowedWorkers` (confirmed in server source); hang/double-execution after a timeout remains (one session = one worker, so a retry after an ambiguous timeout opens a new worker and may rerun).
- Units of results and inputs unknown; wrong units silently corrupt recommendations.
- Model size and transfer time (8 MiB cap in POC); stale "current" model selection.
- Licence expiry mid-operation; transitive WCF crypto packages carry open advisories (the opt-in project downgrades NU1902/3 to warnings).

## 7. Open questions for the owner
1. Units: the interfaces document none for `SetOperatingPointTopNode` pressure or `SolutionPointData`; are they the model's unit system?
2. Is a calculation idempotent / safe to rerun after an ambiguous timeout?
3. Which functions to expose first (nodal analysis, rod-lift analysis, ESP)? The client currently implements `nodal-analysis` only.
4. Is the ForeSite `ModelFiles` route reachable by the AutoPilot service identity?
5. May `Weatherford.WAMI.Interfaces.dll` be redistributed, or must builds take it from an install?
6. Which Windows identity calls WAMI, and is it in `WCFAllowedGroups`? Is WAMI co-located with the Gateway (required in Edge mode)?
7. Manifest V3 shape acceptable (roles with kind + adapter key)?
8. How many concurrent sessions may AutoPilot take given `MaxAllowedWorkers` (licence-capped, shared with ForeSite)?

## 8. What the POC proves
Offline (fake WAMI, stubbed ForeSite HTTP): multi-connection role binding, pins and hash checks, per-role authority,
input quality/unit/staleness gates, busy retry, licence/down/timeout/cancel/ambiguity semantics, and a module producing
a recommendation from WAMI output. Run `dotnet run --project tests\WellSiteAutoPilot.WamiProviderTests`.
Live WAMI: see section 9. Not proven: live ForeSite route and data reads, non-nodal functions, executor integration.

## 9. Live results (installed WAMI 7.2, router `net.tcp://localhost:5656/Weatherford/IRouter`)
Probe: `WellSiteAutoPilot.Integration.Wami.Probe` (build with `-p:WamiInterfacesPath=<installed Interfaces dll>`, run the built dll with router uri and model file).
- Session open + `Blank38Model.wfl` (15,682 bytes) load: about 0.7 s. Stored model: liquid rate 22182.5 stb/d, bottomhole pressure 3883.9 psia.
- Wellhead pressure override (`SetOperatingPointTopNode(-1, p)` then `DoNodalAnalysis`): 100 psia gives 24027 stb/d; 300 gives 19339; 600 gives 5393. Each call 75-150 ms; repeated runs are identical.
- 11th concurrent session refused immediately with an untyped fault ("workers are already being used"), mapped to `WAMI_BUSY` (not started, safe to retry). Limit is 10.
- The installed 7.2 contract lacks `Status`/`ModelValidationResult` on results (7.7 has them), so the client treats a null operating point as failure.
- Sample module (`Integration.Wami.Sample`) ran end-to-end live: scripted data 300 psia + local `.wfl` + WAMI, producing 19338.7 stb/d and a recommendation with provenance naming the model hash. Set `WAMI_PROBE_SAMPLE=300` when running the probe.

Answers: Q1 units are oilfield (psia, stb/d, MMscf/d, scf/stb, water cut as fraction) regardless of model; Q2 nodal analysis is deterministic and only mutates the session's model, so rerun in a new session is safe (other functions unverified); Q7 manifest V3 shape is implemented as role id + kind + adapter key (owner sign-off still wanted); Q8 10 sessions, one worker each, no queue.
Still open: 3, 4, 5, 6 (identity and `WCFAllowedGroups`) and the live ForeSite model route.