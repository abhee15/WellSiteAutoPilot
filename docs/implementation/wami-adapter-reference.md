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
6. **Not yet probed:** `PerformAnalysis` (needs dyna cards and a full configuration), the other lift types, and `GetWAMIServerDiagnostics`.

## Gaps for the adapter

- Rod-lift function descriptors (`rrl-analysis`) with inputs: configuration source, dyna card, analysis options.
- A config mapper from ForeSite `ModelConfigDTO` to `RRLData`; needs the ForeSite catalog routes (pumping units, cranks, motors, rods).
- Function catalog beyond nodal-analysis (validate-model, ESP, PVT).
- Gateway registration with a Calculation capability.
- Identity and `WCFAllowedGroups`.
- Server version string.
