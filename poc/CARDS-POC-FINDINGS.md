# Surface cards end-to-end POC: findings

Branch `poc/cards-e2e`, off `develop` at 6f0959d. Not for merge.

## Result

A manifest v2 module (`modules/poc/foresite-card-summary`) was configured from WSA against ForeSite well 597 (RPOC_0278) and run in Shadow. It completed with `CARD_SUMMARY_COMPLETED`: CardCount 2, PeakLoad 21832 lb, MinimumLoad 9144 lb, StrokeTravel 124.5 in, NewestCardAge about 35,345 h (the newest card is from 2022).

## What the POC changed

- `ForeSiteCards.cs`: reads card types 1 (CURRENT) and 2 (FULL) from `Dynacards/Dynacard/LatestWithUnits/Lib/{well}/{type}`.
- `EngineeringValue`: optional `StructuredSchemaId` and `StructuredJson`.
- Gateway routes quantity `SurfaceCards` to the card reader.
- Four gates opened for manifest v2 (see gap 2).
- Worker: loads `ILogicModuleV2` and runs `ExecuteStructuredAsync`; non-scalar results are rejected.
- AppHost csproj pins DCP 13.5.1 (local workaround, not for merge).

## Gaps

1. The runtime is scalar-only end to end (`EngineeringValue.Value`, `LogicInputValue`, `LogicModuleExecutionResult`).
2. Four hard gates block V2 modules: worker package host (`MODULE_CONTRACT_UNSUPPORTED`), `LogicModulePackageCompatibility.ValidateHost`, `ConfiguredExecutionSnapshot.Create`, and the read-plan quantity allow-list in `ExecutionRepository.Reads.cs`.
3. NATS read replies are capped at 8192 bytes in `GatewayDataClient`. The 2-card sample is about 2.8 KB; larger sets or downhole cards will not fit.
4. Results persist only scalars. Structured or curve outputs have no storage or inspection path.
5. ForeSite data quirks: latest cards are from 2022; surface points are arrays but downhole points are strings; units are `in`/`lb` (not `lbf`); card types are numeric (only 1 and 2 confirmed); there is no per-card quality code.
6. A collection timestamp must not hide per-card capture times. The POC uses the oldest card.
7. The `modules/samples/structured-cards` sample assumes `in`/`lbf`.
8. Activation requires an `If-Match` header (`"none"` when no revision is active). This is undocumented.
9. The DCP 13.0.0 pin is a P4-box-only divergence.
10. Structured checks from #304 were offline only until this POC.

## Recommendations

1. Add a structured value contract: a typed payload plus schema id on `EngineeringValue` and `LogicInputValue`, and structured results, with persistence.
2. Replace the scattered `ManifestVersion == 1` checks with one shared supported-versions check.
3. Make the read-plan quantity allow-list data-driven, from adapter capabilities.
4. Chunk read replies, or move large payloads to the Object Store, before supporting downhole cards.
5. Map ForeSite card types by name and confirm types 3-8 against ForeSite.
6. Normalize units (`lb` to `lbf`) in the adapter, or let the schema declare accepted units.
7. Carry per-card timestamps and keep age limits explicit. The default `maximumAgeSeconds: 60` can never pass against ForeSite's roughly 30-minute scan.
8. Document the `If-Match` activation token in the API.
9. Add an end-to-end CI-style test of the card path (adapter, NATS, worker, result).

## Handoff: building on this POC

### Generated web API (not committed)
`src/web/src/api/generated/v1` is generated, not tracked, so a fresh checkout has no web client. Any branch that builds the web app (or the UI for cards) must generate it first:

1. Start the AppHost once. The Server writes `artifacts/openapi/v1/server.json` on startup.
2. `cd src/web; npm ci; npm run generate-api` (`openapi-ts -i ../../artifacts/openapi/v1/server.json -o src/api/generated/v1`).
3. Re-run step 2 after any server contract change (for example, a new structured-value DTO).

The POC itself adds no new API surface, so the generated client is unchanged by it.

### Fresh worktree setup
- Copy the untracked `dev/WellSiteAutoPilot.AppHost/aspire.config.json` from a working tree.
- Create the gitignored `dev/WellSiteAutoPilot.AppHost/appsettings.Local.json` (own database name, NATS store path, bootstrap admin, Negotiate auth).
- Keep the `Aspire.Hosting.Orchestration.win-x64` 13.5.1 pin in the AppHost csproj on machines where DCP 13.0.0 never launches child processes (symptom: NATS stuck in `Starting`). Drop it on machines that do not need it.
- Start with `$env:Providers__ForeSite__BaseUrl='http://<foresite-host>/'`, then `dotnet run --project dev\WellSiteAutoPilot.AppHost -c Release --launch-profile Windows`. Ready at `http://localhost:5080/health/ready` after about 30 s. Only one stack can run at a time (shared ports 5080-5082).

### Reproduce the run
1. `POST /api/v1/connections` with `{name, adapterKey:"foresite", settings:{baseUrl}}`, then `/test`, `/discover`, `/import/preview` and `/import` (well 597, `kind: foresite.well`).
2. Build the module: `modules/poc/foresite-card-summary` (`module validate`, `publish`, `pack` produce `artifacts/card-summary.wsamodule`; the artifacts folder is gitignored).
3. `POST /api/v1/logic-modules/packages` (octet-stream), then `POST /api/v1/logic-modules/{moduleId}/1.0.0/approve` with a reason. Poll `package-state` until `objectAvailable` is true.
4. `POST /api/v1/configured-logic` with the module manifest, an asset binding (`role: well`) and a data binding (`requirementId: cards`, `providerId: foresite`, `providerAssetExternalId: 597`).
5. `/revisions/{rev}/validate`, then `/activate` with header `If-Match: "none"`, then `/shadow` with `{requestId}`. Read `GET /api/v1/executions/{id}`.
6. Offline adapter check: `poc/CardProbe` (`dotnet run -c Release -- http://<foresite-host>/ 597`).

### ForeSite facts to build against
- Cards are under `Dynacards/`. Use `Dynacards/Dynacard/LatestWithUnits/Lib/{well}/{type}` and `Dynacards/Headers/Latest/{well}/{types}`.
- Well 597 has types 1 (Current) and 2 (Full). Types 3-8 return no cards. Names for 3-8 are unconfirmed.
- Auth is a cookie session via `TokenService/CreateToken`.
- Units come from the response Units block (`in`, `lb`).

### Suggested order of work on GitHub
1. Structured value contract (recommendation 1), covering DTOs, persistence and result storage. Regenerate the web client afterwards.
2. Shared manifest-version check replacing the four gates (recommendation 2).
3. Card adapter, productionized: type-name mapping, unit normalization, per-card timestamps (recommendations 5-7).
4. Payload size strategy (recommendation 4), before downhole cards.
5. UI for viewing structured inputs and results, using the regenerated web client.
