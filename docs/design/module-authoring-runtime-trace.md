# Module Authoring — runtime-backed intermediate calculation trace

Status: **design / contract investigation** · 2026-11-17 · target `develop`.  
This document does **not** authorize executing modules in the browser or claim a shipped trace.

## Why this is the next increment

The workbench already exposes typed input/output declarations and a read-only execution boundary. It must not infer internal algorithm edges from a module's C# or Python implementation.

**Verified implementation:**
- `src/modules/WellSiteAutoPilot.ModuleSdk/StructuredModuleContracts.cs` — `LogicModuleExecutionResultV2` returns `OutcomeCode`, `EngineeringResults`, `Recommendations`, `ControlIntents`, `StateUpdates` and optional `FailureCode`; **no intermediate step trace**.
- `src/server/WellSiteAutoPilot.Application/Logic/StructuredModuleOfflineHost.cs` validates reviewed input/output schemas, freshness/quality, bound envelopes and result failure before returning a structured result. It cannot infer code-internal operations.
- `src/workers/WellSiteAutoPilot.Worker.DotNet/Execution/ModuleExecutionEngine.cs` currently serializes V2 result outputs and `StructuredInputs` into the execution's output JSON (`ValueContractVersion: 2`), but no internal step evidence.
- `modules/samples/structured-cards/CardCalibrationModule.cs` computes load adjustment with `load × loadScale + loadOffset`, each card's adjusted maximum, and an aggregate maximum, returning `CalibratedCards` and `PeakLoad`. The tests' **synthetic** fixture has `loadScale: 2`, `loadOffset: 10` and `PeakLoad: 490 lb`; this is **not** downhole-card analysis.

The production hosted structured path currently supports a narrow authorized ForeSite `SurfaceCards` read. ForeSite quality may be `Unknown` (rejected by the reviewed policy), and the sample's calibration record is synthetic; the example does not establish provider-connected production readiness.

## Principle: a trace is explicit evidence, never inferred UI state

A trace is optional **module-reported data validated and associated by the host**. Validation proves that the envelope has a bounded, well-formed structure; it **does not prove the algorithm's mathematical correctness**. The UI must say "Module-reported calculation" when step evidence is present, and "Intermediate trace unavailable" otherwise.

Separately, authorized input observations carry source, quantity/schema, asset role, observation time, retrieval time, quality and provider/capability identity as permitted. Never replace the card's `capturedAtUtc` with observation or retrieval time. Never copy provider credentials, secret connection identifiers, or request tokens into trace evidence.

## Proposed optional V1 step-evidence envelope (not yet implemented)

Define a versioned, optional structured trace emitted by reviewed module code and validated in the trusted execution host. The host binds it to `ExecutionId`, approved package checksum, module ID/version and manifest revision; these identity fields **must not** be accepted from free-form module claims.

Each ordered step should contain:
- Stable `stepId` scoped to one module revision, a bounded display label/kind (`transform`, `aggregate`, `decision` or explicitly defined extension), and `status` (`completed` or a typed failure).
- Optional references to **declared input requirement IDs**, **prior step output IDs** and **declared result IDs**. These are *specific recorded dependencies*, not inferred links. Validate references, uniqueness, ordering and acyclicity; the platform must never invent missing paths.
- Optional schema-identified intermediate value(s), unit and an explicit source (module-reported or host-observed). Scalar summaries can be displayed first. Full arrays/curves require a reviewed schema and size bounds, not arbitrary text serialization.
- Omit intermediate values by default for modules that do not opt in, including V1 modules and current Python modules. The trace must not change the module outcome, affect recommendations/controls, or bypass approved package/asset authorization.

**Proposed validation limits for initial implementation**: at most 64 steps and at most 64 references per step, with the existing 256 KiB module result envelope as an upper bound. Review lower UI/display limits separately; no unbounded array rendering or automatic expansion of every step. Reject invalid, oversized, duplicate or cyclic trace data with a clear contract failure rather than silently dropping it. Keep the existing host's post-failure policy: results from a failed evaluation must not appear as successful evidence.

## Incremental implementation and acceptance

1. **SDK + offline host only** — add an optional trace record alongside `LogicModuleExecutionResultV2`, validate version/IDs/references/shape/bounds before returning. Keep old modules deserializable and the existing failure-code behavior intact. Tests cover valid/invalid steps, no trace, duplicate/unknown references, cycles, oversized data and failure handling.
2. **Real synthetic algorithm trace** — instrument the existing structured-card calibration module explicitly. Capture the recorded adjustment and per-card peak / aggregate stages from its code; prove the final scalar is `490 lb`, paired arrays retain order, and rejected samples never produce a successful trace. Do **not** compute these values in React.
3. **Worker transport/evidence** — version the optional trace's JSON representation in `ObservedExecutionResult.SerializeOutput()`, preserve compatibility with V2 execution results and canonical terminal/replay semantics, and test persistence and authorization. Do not transmit it in error paths as a success.
4. **Authoring test/preview UX** — for an authorized execution or reproducible offline sample, show a focused selected-step trace: recorded inputs → selected transformation → recorded intermediate → final output. Include source/status, units and exact timestamps when present; provide an explanation for missing or rejected evidence. Preserve the current compact editor, `GuidedModuleContract` port selector, semantic colors, accessible disclosure, and existing tokens. No extra permanent canvas.
5. **Provider rollout** — use Provider Simulator and worker integration in CI; keep live ForeSite/CygNet acceptance and other provider capabilities explicitly pending until verified on the user's local licensed systems. Later exercise model-artifact + WAMI calculation roles independently.

## Design and safety gates

- Selecting an output shows upstream connections **only when recorded references exist**. A code-backed step without instrumentation remains one opaque step.
- Prevent mixing one execution's trace with another draft, asset, package SHA, revision or test scenario. Any changed source/draft invalidates earlier evidence.
- Explicitly distinguish **synthetic fixture**, **authorized live read**, **offline module evaluation**, **hosted Shadow run**, and **approved deployed execution**. A preview is not approval.
- Keep traces readable at 1440/1024/768 CSS px and keyboard-accessible. Reuse `PanelHeader`, `Notice`, `ActionButton`, spacing and status semantic tokens; do not introduce a second UI framework or duplicate scroll areas.
- Before shipping the SDK contract, review compatibility of the V2 output JSON, external result consumers, retry/idempotency semantics, audit permissions, tenant isolation and sensitive value redaction.

This plan intentionally makes **recorded intermediate calculation evidence** a separate contract slice from the already-shipped input/output declaration projection.
