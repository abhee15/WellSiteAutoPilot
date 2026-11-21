# Live-system POC reconciliation for adapter capability development

Status: implementation recommendation, 2026-11-07. Source of evidence: `docs/provider-logic-module-gaps` (handoff index, G1-G26 register, provider-specific references, recorded fixtures) and the **individual linked POC branches**. This document is not live provider acceptance and is not a request to merge experimental branches wholesale.

## Source of truth and interpretation

Read [the handoff index](https://github.com/abhee15/WellSiteAutoPilot/blob/docs/provider-logic-module-gaps/docs/implementation/poc-handoff-index.md) on branch `docs/provider-logic-module-gaps`, not the older `develop` `docs/implementation/poc-gap-register.md`. The docs branch contains recorded real ForeSite, CygNet and WAMI evidence in `docs/implementation/fixtures`. The POC branch snapshots are historical; `develop` now includes #485 (typed operations/history), #488 (CygNet native history candidate), #490 (connection/asset eligibility) and #491 (static capability-first UI, typed ForeSite well details). Compare the actual implementation paths before porting any POC.

An adapter's **static implemented capability reference** is independent of Connection/Asset selection. A later **per-asset available quantity/catalog** is a runtime query with explicit authority, source identity and revisions. A **hosted module binding** is a separate permissioned runtime ability. Never conflate these three. The existing `AdapterDescriptor` and `AdapterOperationDescriptor`, versioned groups, Gateway and typed provider interfaces remain the owners. No string-based ExecuteOperation layer or second registry.

## Complete inventory of linked POC branch evidence and treatment

Every branch listed in `poc-handoff-index.md` was compared with `develop` and the provider reference was reviewed. All currently diverge; treat each as selectively portable evidence and tests, not a merge candidate.

| Branch | Evidence / purpose | Treatment against current develop |
| --- | --- | --- |
| `poc/gap-g09-single-status-read` | Session-scoped single status read/cache, source-shaped tests | Review for bounded per-execution read fan-out; do not introduce cross-Connection or stale-status caching |
| `poc/gap-g02-provider-units-ranges` | Units/ranges/precision from ForeSite `Units` block | **First slice**: preserve source unit per quantity, no universal percent/0-100 assumption |
| `poc/gap-g01-more-current-quantities` | `CurrentSPM`, `CyclesYesterday`, `RunTimeYesterday` | **First slice**: explicit allowlist, typed fields and synthetic examples |
| `poc/gap-g12-average-spm` | `AverageSPM` | **First slice** with actual source/units |
| `poc/gap-g23-daily-quantities-sentinel` | Today/yesterday run/idle/strokes/cycles/inferred production, 65535 unset sentinel | **First slice**, only fixture-backed, accurately distinguish missing from legitimate zero |
| `poc/gap-g03-discovered-catalog` | Per-well available quantities and device information | **Next capability**; do not confuse dynamic asset applicability with static adapter reference |
| `poc/gap-g04-readonly-setpoints` | Typed ForeSite saved setpoint groups/units/ranges/read-only flags | **Next capability**; separate read-only contracts and governance from physical writes |
| `poc/gap-g07-readonly-alarms` | `Alarms/CurrentAlarms/{id}` | **Next capability**; bounded typed alarm states, no ack/clear operations |
| `poc/gap-g05-daily-averages` | DailyAverage source response, production-day time edges | **Next history capability**, not a synonym for current or sub-daily history; source zone matters |
| `poc/gap-g10-foresite-simulator` | Fixture-driven replay of recorded ForeSite responses | **First-slice test input** selectively; inspect fixtures before committing/logging or displaying actual values |
| `poc/gap-g05b-time-range-trends` | CygNetTrends read-only POST, quantity-specific units/errors | Already partially served by #485 bounded history. **Compare and augment** supported coverage; never replace typed history with generic provider passthrough |
| `poc/gap-cygnet-udc-catalog` | PNT facility-point catalog, per-point unit and `cygnet.udc.*` names | **Next CygNet slice**; distinct from core portable quantity mappings, permission and bounds required |
| `poc/gap-cygnet-history` | VHS native history and local-time daily-rollup prototype | #488 already carries an opt-in native candidate/test runner. **Reconcile** VHS decoder, quality, DST and bounds; live activation waits for licensed acceptance |
| `poc/gap-cygnet-alarms` | CAS alarm/point status experiments | **Next CygNet capability**; do not report stale CAS entries as fresh controller alarms |
| `poc/wami-baseline` | Typed WAMI client/fake, model + calculation coordinator, nodal sample | **Separate calculation-service path**; not a normal well telemetry source |
| `poc/gap-wami-foresite-model-live` | ForeSite model-file/configuration probes and model-artifact sources | **Separate model-artifact contract**; distinguish .wfl from RRL config and enforce hash/revision/size |
| `poc/gap-wami-license-features` | WAMI per-feature license checks | **WAMI static vs activation distinction**: implement function only when client supports it; license availability is a dynamic connection fact |
| `poc/gap-wami-calc-functions` | Nodal, IPR, PVT and performance-curve POC functions; 7.2 differences | **WAMI typed function catalogs** with source-version/units/special floating-value handling; no fictitious functions |
| `poc/gap-wami-wellflo-model-library` | Sandboxed folder-backed .wfl model source and hashes | **Separate artifact provider**; validate path isolation, provenance and ownership |
| `poc/gap-wami-gateway-registration` | Opt-in WAMI adapter, connection check and Gateway factory | **Future WAMI slice**; adapt to current typed descriptor/catalog and trusted Gateway; no vendor DLL in default build |
| `feature/poc-wami-provider` | Older multi-provider role-binding, authorization and sample end-to-end POC | Architectural input for separately reviewed manifest V3 and runtime composition, not an accepted deployable feature |

Related gaps G6, G8, G11, G13-G22, G24-G26 are covered by the gap document even where there is no corresponding standalone branch: controller-aware setpoint maps, well/device traits, applicability, gauge-off/timezone, actions/verification, and model composition. Their absence from a branch name does not make them implemented.

## First production-quality increment: ForeSite surveillance extension

Deliver a **single focused feature branch/PR off current develop**, building on #491 rather than the old POC stack. Extend the existing ForeSite `Surveillance data` capability's typed current-data operations to explicitly supported `CurrentSPM`, `AverageSPM`, cycles, runtime, idle time, strokes and inferred production (exact final list gated by fixtures and type semantics). Preserve `PumpFillage` and `AveragePumpFillage`. Reuse the existing ForeSite session/token/cookie, revision-scoped Gateway, source validation, `ICurrentDataProvider`, read rules and descriptors.

Acceptance gates:

1. One reviewed, bounded source-shaped status parser; fields are an explicit mapping, not arbitrary user-provided JSON names. Do not duplicate existing auth/session or normalize distinct source quantities to one unit.
2. Unit/range/precision metadata comes from the source `Units` block where present. A fallback is explicitly documented as a fallback, not a provider-verified unit/range. Status `LastGoodScanTime` retains its acquisition meaning; retrieval/`LastScanTime` cannot refresh stale readings. Source results keep quality semantics conservative.
3. Absent/invalid values and the verified 65535 unset sentinel are failures/missing, while numeric zero remains valid; deny ambiguous, malformed and oversized payloads. Keep read and response budgets enforced.
4. Update the **existing adapter-owned capability group and operation descriptors**, typed contracts, documentation examples, provider route/field maps and conformance checks. The generic Adapters page must pick up the new supported operations with **no ForeSite-specific UI switch** and still require no Connection selector.
5. Preserve the separate runtime authorization/read-plan rules. Adding a static declaration must not silently grant module consumption, discovery rights or new control authority. Negative tests: unsupported quantity, mismatched source, stale revision, permission denial/revocation, invalid units/timestamps, null vs zero, sentinel, size, concurrency, and shared status read behavior.
6. Include focused synthetic/recorded-shape test fixtures (redact/review before copying), provider/Gateway integration, generated client validation, frontend interaction/responsive checks, full current-head CI, and exact visual review. Live provider testing is user-owned.

This is a substantive **capability extension demonstration**: new provider functions appear inside Surveillance data, API contracts update automatically from adapter declarations, and the server can resolve their typed source reads using existing safeguards. It is **not** a per-Connection live-data preview or new module authoring UI.

## Follow-on slices (separate PRs)

1. **Per-Asset quantity applicability/catalog:** merge the ideas of ForeSite G3 and CygNet PNT without conflating static adapter support and what one well/facility actually supplies. No display of private live data on the static adapter reference page. Source scope, connection revision, role/asset authorization, finite TTL, bounded response, no universal portability claims.
2. **Read-only setpoint catalogs** from ForeSite G4/G11/G19/G24/G26 and CygNet DDS POC: typed names and groups, units/ranges, controller-family mapping, explicit passive read timestamps. No writes, scans, commands, or inferred automatic controller equivalence.
3. **Alarm and history breadth:** ForeSite current/intelligent alarms, DailyAverage and additional CygNetTrends quantities; CygNet CAS and VHS test coverage/activation after local acceptance. Strict timestamp, quality, unsupported and truncation semantics. Reuse #485/#488 instead of replacing them.
4. **WAMI calculation-service adapter:** after catalog/contract review, port only the opt-in client/fake and narrowly verified function descriptors; add separate model-artifact and session-license contracts. Then independently review per-role authorization, pinned model hash, versioned module dependencies and execution-host integration. No immediate V3 rollout and no direct module provider I/O.
5. **Command providers:** keep outside these slices pending reviewed governance, stale-baseline recheck, explicit authority/opt-in, idempotency/ambiguity handling, rate limits, kill switch and readback. The POC describes possible APIs; live writable capability is not implied.

## Reconciliation rules

- Every linked POC is evidence of a specific *tested scope*, not evidence of production-safe implementation. Compare branches against current `develop`, port specific tests/logic selectively, and document omissions.
- Provider capability **implemented** differs from per-Connection **healthy/licensed** and per-Asset **available**, and from module-host **consumable**.
- Never infer the same Asset from equal external IDs in ForeSite/CygNet, or treat WAMI session-model mutation as a physical controller command.
- Review source fixtures for sensitive data before adding them to default CI, screenshots or examples.
- Separate provider-specific code from shared contracts, and avoid provider switches in frontend rendering.
