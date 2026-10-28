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
