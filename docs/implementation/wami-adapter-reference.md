# WAMI adapter reference (what WAMI offers and what the adapter can call)

Status: POC findings, probed read-only against the local WAMI server (`net.tcp://localhost:5656/Weatherford/IRouter`, net472 build from the WellFlo repo). Contract sources: `WellFlo\Trunk\API\Common\WellFlo.API.Interfaces\ServiceContracts\`.

## How WAMI is called

- One WCF net.tcp router; each contract is an endpoint under it (`.../IRouter/IModel`, `/ILicense`, `/IReciprocatingRodLift`, ...). Windows transport security.
- A channel is a stateful session: the server keeps the loaded model or configuration for the life of the channel. Limit 10 concurrent sessions, one worker each, no queue; the 11th gets an untyped fault (`WAMI_BUSY` in the adapter).
- Units are oilfield (psia, stb/d), regardless of the model.
- The installed 7.2 contract has no `Status` or `ModelValidationResult` members that the ForeSite 7.7 build has.

## Contract inventory (public methods)

| Contract | Methods | Purpose | Adapter status |
|---|---|---|---|
| `ILicense` | 14 | Per-feature licence checks (`HasRRL/ESP/GasLift/PlungerLift/PCP/FlowAssurance/RRLGibbs/RRLEJ/RRLDesignVertical/RRLPatternMatching`), `GetCurrentLicense`, server and worker diagnostics | Feature checks used by `GetServerInfoAsync`. Diagnostics not yet used (candidate health signal) |
| `IModelFile` | 7 | `OpenFileData`, `GetFileData`, `NewModel`, application and file version | `OpenFileData` used |
| `IModel` | 71 | Nodal analysis: set/get/validate wellbore, surface, reservoir, fluid, IPR tuning; `DoNodalAnalysis`, performance curve, PVT, flow assurance, multiflash | `DoNodalAnalysis`, `SetOperatingPointTopNode` only |
| `IReciprocatingRodLift` | 52 | Rod-lift configuration (surface, counterbalance, motor, pumping unit, downhole, rodstring), dyna cards, `PerformAnalysis`, downhole card, pattern matching | Not yet |
| `IESP` | 19 | Electric submersible pump | Not yet |
| `IGasLift` | 36 | Gas lift | Not yet |
| `IPCP` | 30 | Progressive cavity pump | Not yet |
| `IPlungerLift` | 17 | Plunger lift | Not yet |
| `IConfiguration` | 20 | General data, well specification, deviation survey | Not yet |
| `IReservoir` / `ISurface` / `IWellbore` | 155 / 56 / 12 | Fine-grained reservoir, surface equipment and wellbore data | Not yet |
| `IInjection`, `IValidation` | 2, 1 | Injection, validation | Not yet |

## Findings

1. **Nodal analysis works end to end.** Local Blank38Model: 22,182 stb/d; operating point stable, interpolated, refined.
2. **All ten licence features are valid on this box.**
3. **Rod-lift needs no model file.** A fresh session on `IReciprocatingRodLift` returns `null` from `GetConfigurationData` until a configuration is set. `SetConfigurationData(new RRLData{...})` returns an empty validation list and the values read back unchanged (downhole pump diameter, depth, tubing and casing sizes). So an RRL calculation does not need a `.wfl`.
4. **ForeSite already does this conversion.** `ModelFileService.GetRRLData(ModelConfigDTO, ICatalogService, IReciprocatingRodLift)` builds an `RRLData` from the ForeSite `ModelFiles/ModelConfig/{wellId}` JSON, using catalog lookups (pumping unit, crank weights, motor, rods), then calls `SetConfigurationData`, `SetAnalysisOptions` and `SetWellSpecification`. The WAMI adapter should reuse that shape: take ForeSite's structured config, not the 1,135-byte text blob in the model file.
5. **ForeSite's `ModelConfig` for well 776 has zero downhole values** (pump diameter, depth, tubing sizes), while surface data is populated. An analysis needs downhole data from somewhere else, or the well is not fully configured.
6. **Calculation APIs probed live on a WellFlo model (Blank38Model, private session, nothing saved):** `OpenFileData` (opens in about 1.4 s), `ValidateModel` and per-section validators (this blank model reports missing tubing and terrain rows even though nodal analysis solves it), `GetNodalAnalysisOption`, `GetOperatingPointStartNodeList`, `GetIPRFlowRate` (59,624 stb/d at 2,000 psia), `GenerateWellPerformanceCurve` (about 190 ms, rate and wellhead pressure points), `CalculatePVTData` (bubble point 4,569 psia, Rs 356, Bo 1.159, z 0.856, oil viscosity 1.19), `DoNodalAnalysis` (about 120 ms). Calls after the open take 80 to 200 ms. The PVT and curve result members have no documented units; the adapter labels them in oilfield units, to be confirmed against WellFlo.
7. **Not yet probed:** rod-lift `PerformAnalysis` (needs dyna cards and a full configuration), ESP, gas lift, PCP, plunger lift, flow assurance, multiflash, IPR tuning, and `GetWAMIServerDiagnostics`.

## Built so far (POC branches, stacked)

- `poc/gap-wami-license-features`: per-feature licence discovery.
- `poc/gap-wami-calc-functions` (`0a1f9e07`): adapter functions `ipr-flow-rate`, `pvt-properties`, `well-performance-curve` (read-only; curve points returned as `curve[i].*` outputs, 2 to 100 points) next to `nodal-analysis`. Live results match the direct probe. `validate-model` was left out because the installed 7.2 contract lacks `ModelValidationResult`.
- `poc/gap-wami-wellflo-model-library` (`1e215dc8`): `WellFloModelLibrary`, an `IModelArtifactSource` over a folder of `.wfl` files, so a logic module can list and fetch a WellFlo model by relative id (pinned SHA-256, last-write time as revision; ids outside the folder and non-`.wfl` files refused). The WellFlo repo has no model service, only files (169 `.wfl` files, mostly test models), so a folder is the stand-in.

## Runtime facts the builder needs

- Endpoint: `net.tcp://<host>:5656/Weatherford/IRouter/<Contract>` (`IModel`, `ILicense`, `IReciprocatingRodLift`...). Windows transport security; caller must be in `WCFAllowedGroups` (default `Users, Administrators`). Timeouts 6 min; very large message limits.
- One channel is one stateful session, backed by one COM worker. Limit 10 sessions (licence-capped, shared with ForeSite), no queue: the 11th gets an untyped fault ("workers are already being used"), mapped to `WAMI_BUSY` (`NotStarted`, retry with backoff). Always dispose the session. A timeout after dispatch is `Ambiguous`.
- Units are oilfield (psia, stb/d, MMscf/d, scf/stb, water cut as a fraction) regardless of the model.
- `SetOperatingPointTopNode(-1, p)` then `DoNodalAnalysis`: wellhead 100 psia gives 24,027 stb/d, 300 gives 19,339, 600 gives 5,393 (Blank38Model). Results are deterministic, so a re-run in a new session is safe.
- Function inputs used by the adapter: `ipr-flow-rate` (bottomhole pressure psia), `pvt-properties` (pressure, temperature, GOR, water cut, flag as in `CalculatePVTData(2000, 150, 1000, 100, 1)`), `well-performance-curve` (points 2-100).
- Opt-in build: `-p:WamiInterfacesPath=<Weatherford.WAMI.Interfaces.dll>`; the contract source is `WellFlo\Trunk\API\Common\WellFlo.API.Interfaces\ServiceContracts`. ForeSite's `WAMIBinding` and the WellFlo `WamiClient` App.config show the binding.
- Result envelope: scalar outputs with provenance `wami:{conn}@{rev}:{function}:{sha12}`; `ResultStatus` non-success is a failure, not a value.

## Gaps for the adapter

- Function catalog: ESP, gas lift, PCP, plunger lift, rod-lift analysis, validate-model (once the contract version allows).
- Series outputs: the result type carries scalars only, so curves are flattened into indexed keys; decide whether to add a typed series result.
- Gateway registration with a Calculation capability.
- Identity and `WCFAllowedGroups`.
- Server version string.
- The ForeSite-to-WAMI rod-lift mapping is deliberately parked (ForeSite model configuration is out of scope for now).
