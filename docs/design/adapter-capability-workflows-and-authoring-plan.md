# WellSite AutoPilot — Adapter capability framework and authoring-readiness plan

**Date:** 2026-11-07  
**Repository baseline reviewed:** `abhee15/WellSiteAutoPilot`, branch `develop`, SHA `178bb32610a3ffb63ff7980ef768683f88ce7647` at review.  
**Reference branch:** `docs/provider-logic-module-gaps`; begin with `docs/implementation/poc-handoff-index.md`.  
**State:** Three design review passes completed; implementation sequenced below. This document is a plan, not evidence that unmerged functionality is shipped or that licensed live acceptance has completed.

## 0. Product principles and boundaries

1. **Provider-native behavior; adapter-owned integration; platform-governed execution.** ForeSite, CygNet, WAMI remain the systems of record for their own data, calculations, and models. We use their established, proven APIs; no parallel surrogate data store or generic HTTP scripting executor.
2. **Design from Logic Module author needs:** find applicable inputs; read/compute them using provider-recommended sequences; preserve types, timestamps, units and result states; validate applicability, availability, quality/freshness and completeness; give stable, actionable failures.
3. **Keep the existing AdapterDescriptor / versioned OperationGroups / typed AdapterOperationDescriptor / existing generic two-pane UI and gateway.** Extend typed metadata with optional, versioned *descriptive* workflows. Never create a second registry or allow user-authored arbitrary provider requests.
4. **Static adapter reference is Connection-independent.** It shows *implemented adapter support* and documented provider workflows, including explicit gaps and consumption readiness. Real applicability/permissions/mappings are checked against an authorized Connection, Asset and execution. Reference screen does not test a Connection or call live APIs.
5. **Workflow guide versus execution:** guide steps can mention provider-native endpoint calls, but they are documentation. Only installed and tested typed adapter operations are advertised as callable. Show a clearly labeled *planned/unwired prerequisite* if an example step is not yet enforced by production code.
6. **Provider roles are extensible.** Modules may use one, two or more connections/providers for data, model artifacts and calculation services; a single provider may fulfill several roles. The future authoring design must not enforce a two-provider ceiling. It must impose explicit reviewed budget/role caps, independent authorization per role and explicit asset identity binding. Manifest V1/V2 remain compatible; V3 POC is not currently runnable on develop.
7. **No unreviewed writes.** Provider commands, scan-triggering setpoint reads and autonomous writes stay behind separate governance; read-only metadata must never imply command permission.
8. **No licensed binaries in normal CI.** CygNet/WAMI opt-in client seams, simulator/fixture tests in CI; user-led local licensed tests separately tracked. Version-dependent WAMI units/result shapes are not guessed.

## 1. Evidence and baseline cross-check

### Existing `develop` contracts to retain
- `AdapterDescriptor`: `Operations` and `OperationGroups`, `ReadOperations` filters supported read kinds and major v1, no discovery/command rows. Exact-key duplication and invalid references must fail closed (check ongoing PR #502).
- `AdapterOperationDescriptor`: typed request/response, capability ID/version, timestamp/quality semantics, bounds, synthetic example docs. `AdapterOperationDocumentation` currently has one `ProviderReference` string, one invocation and synthetic request/result JSON; this is insufficient for ordered workflows.
- `ConnectionMapping.ToOperations`, `AdapterOperationResponses`, `AdapterCapabilityDetails.tsx`, `adapterOperations.ts` are the single DTO/projection/UI path. Reuse this; add negative validation tests and versioning.
- `ProviderContracts`: `ICurrentDataProvider`, `IHistoricalDataProvider`, `ICalculationProvider`, `EngineeringValue`. History extends with `IBoundedHistoricalDataProvider`, 31-day/1000-sample/1-MiB source ceilings; preserve `Truncated`, `Aggregation`, `Diagnostics`.
- `AdapterReadRules` currently authorizes ForeSite only `PumpFillage` and `AveragePumpFillage`, and CygNet five fixed logical quantities. Expanding descriptors without executable rule/contract updates would misadvertise support.
- `LogicModuleManifestValidator` supports manifest V1/V2, data access `Current|History`, V2 reviewed structured inputs. It does **not** support multi-provider V3, direct well-test or calculation requirements. A Gateway implementation is **not** equivalent to a hosted module binding.
- Current ForeSite adapter exposes Surveillance (2 quantities), Well configuration, Well trends (bounded PumpFillage only), Surface Card (CURRENT/FULL). ForeSite card quality is `Unknown` and current reviewed runtime policy rejects Unknown, pending an explicit quality decision; `lb` is not automatically `lbf`. Current WAMI adapter is not in `develop`.

### POC branch inventory (all handoff references reviewed)

| Family | Branch or lineage | Established purpose and what to port |
|---|---|---|
| ForeSite | `poc/gap-g09-single-status-read` | One controller status document per compatible execution; review 3-second cache isolation |
| ForeSite | `poc/gap-g02-provider-units-ranges` | Units/precision/ranges from `Units` where supplied |
| ForeSite | `poc/gap-g01-more-current-quantities` | CurrentSPM, CyclesYesterday, RunTimeYesterday |
| ForeSite | `poc/gap-g12-average-spm` | AverageSPM |
| ForeSite | `poc/gap-g23-daily-quantities-sentinel` | Daily-cycle, stroke, runtime, idle-time, inferred-production fields; 65535 unset |
| ForeSite | `poc/gap-g03-discovered-catalog` | Per-well catalog; **correct** its populated-value-only discovery against provider `AvailableQuantities` IDs |
| ForeSite | `poc/gap-g04-readonly-setpoints` | Passive saved setpoint groups, typed/range/readonly; controller-specific logical-name mapping remains |
| ForeSite | `poc/gap-g07-readonly-alarms` | Current alarms, suppression/status/event timestamp |
| ForeSite | `poc/gap-g05-daily-averages` | Provider production-day boundaries; don't use server-local midnight |
| ForeSite | `poc/gap-g10-foresite-simulator` | Recorded-response simulator for negative/positive tests |
| ForeSite | `poc/gap-g05b-time-range-trends` | POST CygNetTrends; **reconcile** with #485 production bounded PumpFillage history, don't duplicate |
| CygNet | `poc/gap-cygnet-udc-catalog` | PNT per-facility UDC catalog, point units and metadata |
| CygNet | `poc/gap-cygnet-history` | VHS points, decoding reliability, calendar-day rollups; reconcile with current native-history candidate |
| CygNet | `poc/gap-cygnet-alarms` | CAS alarms; stale-but-active possible |
| WAMI | `poc/wami-baseline` | Typed contracts, fake, NetTcp opt-in, coordinator POC |
| WAMI | `poc/gap-wami-foresite-model-live` | ModelFiles response differs; ForeSite `foresite-rrl-config` is not `.wfl` |
| WAMI | `poc/gap-wami-license-features` | Per-feature license probes, version differences |
| WAMI | `poc/gap-wami-calc-functions` | Nodal, IPR, PVT, performance curves; scrutinize units and no-solution statuses |
| WAMI | `poc/gap-wami-wellflo-model-library` | Folder-backed `.wfl` source, bounded IDs and SHA-256 model pins |
| WAMI | `poc/gap-wami-gateway-registration` | WAMI descriptor and opt-in factory/connection check; **not** current runtime readiness |
| Prior composition | `feature/poc-wami-provider` | Named Data/Model/Calculation roles, independent grants/pins and validator-only V3 concept; do not wholesale merge diverged branch |

All 20 branch refs above are **POCs**, not evidence of current `develop` integration. Reference docs contain real live-system findings, while fixtures can be synthetic/sanitized/archived; never equate a passing fixture test with present live system success.

## 2. Data semantics required for all adapters

Preserve these as distinct dimensions, not one boolean `Available`:
- **Adapter implemented:** compiled/installed typed operation and descriptor.
- **Provider compatible and reachable:** tested for explicit backend/identity/version, independently of instance-specific support.
- **Asset applicable:** source catalog says a requested quantity/function applies to this exact authorized source; for ForeSite `AvailableQuantities` IDs must map to verified enum identities, and well/device type must be considered.
- **Mapped/available:** a physical point/field/model/test exists and is readable; empty/null/unmapped is not the same as not applicable.
- **Value valid:** numbers, sentinel, provider quality, result/test status, units and structured payload pass checks.
- **Fresh/representative:** acquisition timestamp and measurement period/event are preserved separately when they differ; test the module's explicit age/coverage/timing constraints. Retrieval time never refreshes a stale sample.
- **Module-bound:** the host actually supports this requirement and source under immutable manifest/read plan and current authorization. Never infer from a static catalog row.

Engineering values and series require explicit source time kind (controller scan, point scan, historical sample, card capture, well-test sample, config change, calculation result), retrieval time, optional `observationPeriodStart/End` / test event timestamp when meaningful, source quality/engineering status, units and revision. Do not break existing V1 scalar transport; add fields/typed alternatives conservatively.

## 3. Provider/capability workflows

### ForeSite Surveillance
- **Find applicable:** GET `Surveillance/AvailableQuantities/{wellId}`; it returns enum IDs. Resolve against verified ForeSite enum for the deployed provider version. Do not treat status field presence or a non-null reading as proof of applicability; support zero values. Well and device type are separate constraints.
- **Acquire:** GET `Surveillance/well/{wellId}` once per execution/snapshot, extract requested `Value` fields and source `Units` block; validate limits/precision and stable mapping. Preserve the status document's common `LastGoodScanTime` for the controller-scanned subset, never `LastScanTime`; note per-field exceptions where proof exists. `*Yesterday` values represent prior production day even if acquired on a current scan.
- **Validate:** unset `65535` for specified stroke quantities, missing numeric values, non-finite/invalid range, future scan and maximum age. `Good` denotes successful acquisition of valid controller value, not independent sensor validation.
- **Contract:** semantic quantity catalog (not asset import discovery), snapshot-aware multi-input read, typed result with source timestamp/period and failure codes. Cache catalog by connection revision/asset/source config with TTL and invalidation. Do not reuse status across unrelated execution contexts without proof.

### ForeSite Trends / Daily
- **Raw history:** POST `Surveillance/CygNetTrends/{wellId}/{start}/{end}` with verified provider enum quantity names; `PumpFillage` currently maps to `CurrentPumpFillage`. Validate explicit UTC instant bounds and per-quantity result errors. Preserve per-sample source time/unit (runtime can be `h` on trend vs `s` in status), `Unknown` quality where no source status given, complete/partial/truncated semantics. Avoid arbitrary selection/order or silent unit coercion.
- **Daily averages:** GET `Surveillance/Well/DailyAverage/{wellId}/{start}/{end}`; retain provider production-day Start/End (not UTC calendar day); reject broken intervals. `*Yesterday` controller rollover fields are not interchangeable with a derived daily average.
- **Contract:** distinct raw-series vs provider-daily operations with time-window and coverage semantics; existing `HistoricalRead` bounds apply or require an explicitly reviewed new version.

### ForeSite Well Test
- API evidence: `WellTests/GetLatestWellTestData`, `WelltestQuantities/{wellType}` and test-history route candidates; verify exact verbs/parameters against service contracts before shipping. Distinct `WellTestData` typed family, not `CurrentData` and not `ConfigurationData`.
- **Critical evidence:** recorded `WellTests_GetLatestWellTestData_1596.json` has `SPTCodeDescription=RejectedTest`, `ValidationStatus=Oil rate above acceptance limit` despite numeric Oil/Water values. A `latest` test is not necessarily an accepted test. Preserve `SampleDate`, `TestEndDate`, source revisions/approvals and status meanings; do not substitute `LastChangedDT` or retrieval time for measurement time. Validate accepted selection, null/zero/type and well type applicability. Initial reads metadata/tests only until acceptance semantics are verified.

### ForeSite Cards
- CURRENT/FULL surface cards: GET `Dynacards/Dynacard/LatestWithUnits/Lib/{id}/{type}`, current bounded structured contract. Preserve per-card capture time and paired position/load arrays, sample order, oldest-card age, units `in`/`lb` and `Unknown` quality.
- **Open blocker:** reviewed V2 structured-card module input rejects Unknown quality; sample manifest currently requires `lbf` but source carries `lb`. Resolve via explicit reviewed policy/conversion or fix sample, without blindly promoting quality. Downhole cards and larger payloads are separate future investigations.

### ForeSite Setpoints / Alarms (read-only)
- Passive saved setpoints: GET `Surveillance/Well/AllSavedSetPointParams?WellId={id}&scanOption=0`, group + element + original/read-only/range + device-specific map. `scanOption=0` is not a live device refresh. No `Send`, UIS command or scan-triggering operation without approved governance.
- Active alarms: GET `Alarms/CurrentAlarms/{id}`, preserve `StartTime`, suppressed and deviation flags; no unverified auto-clear/write semantics. Active status and old event age can coexist.

### CygNet Current / Catalog / Historian / Alarms
- Use PNT `GetPointTagList` / point records to discover facility-scoped point UDC, data type, unit, description; verify site/service identity. CygNet has no ForeSite-style `AvailableQuantities`. A point catalog entry alone is not grant to execute arbitrary `cygnet.udc.*` operations; introduce constrained namespaced allowlists and authorization/mapping policy.
- Read native current values through installed client. Decode type/status/quality/source timestamp; distinguish stale/unreliable/no-value and numerical vs digital point. Use existing bounded synchronous call gate, and account for per-point vs batch scan atomicity.
- VHS `GetHistoricalEntriesReverse` on full point tag; source byte[] decode by type, UTC assumption from POC needs licensed host confirmation. Reconcile production 1000-result/10,000-scanned/1-MiB ceilings with POC 50,000-entry behavior; exact truncation/reliability/incomplete-coverage outcomes matter. Calendar-day rollup has zone/DST rules; reject NaN/no-valid-samples.
- CAS alarm events are read-only and may be active despite an old timestamp. DDS passive setpoint reads are different from point reads; per device-type map required, without triggering command transactions.
- CygNet well tests/card provider APIs were **not established** by reviewed POCs: do not declare support. Note earlier Bridge API vs native transport recommendation and identity/licensing decisions; current native candidate is not live validated.

### WAMI Function / Model / Composition
- WAMI is a calculation dependency, not a telemetry point provider. `ILicense` feature check, select compatible contract endpoint, open a fresh stateful session/model or set RRL configuration, set inputs, execute named function, validate engineering result/status, release session. Not all calculations require `.wfl`; ForeSite RRL config is not a `.wfl`.
- POC live verified nodal/IPR/PVT/curve, features/licensing, max ten concurrent workers on local test server. That limit is environment evidence not universal default. `no operating point` is a valid *no-solution* result, not numeric zero. Sanitise NaN/Infinity. WAMI 7.2 and 7.7 contracts differ; validate unit/contract version and result shapes before advertising.
- Model artifacts need source format, applicable version and SHA-256 pin; prevent path traversal and unsupported formats. WAMI operations can alter **session model** even when they do not write persistent ForeSite source; explicitly label side effects.
- Multi-provider planned roles: `data` (ForeSite or CygNet), `model` (compatible source), `calc` (WAMI), potentially several data providers. No implicit matching on external well ID; bind each role to exact authorized Connection, revision, Asset and source. Recheck grants, quality/time alignment, source provenance, model hash and execution outcome. Bounded DAG/pre-acquisition order can be planned later; avoid an arbitrary 2-provider ceiling. No live deployment of V3 yet.

## 4. Versioned workflow-guide extension — first implementation slice

**Keep:** `AdapterDescriptor.OperationGroups`, `AdapterOperationDescriptor`, `AdapterOperationDocumentation`, `ConnectionMapping`, `AdapterCapabilityDetails` and shared tabs/components.

**Add (non-breaking):** an optional `Workflows` collection on `AdapterCapabilityGroup` (or a single optional group-level metadata property) with `id`, `title`, `purpose`, `supportedOperationKeys`, ordered `steps` (`action`, `providerApi`, `condition`, `onFailure`), `providerEvidence`, `runtimeAvailability` and `readiness`. No free-form executable instructions, arbitrary route forwarding, secrets, or dynamic network calls.

- `readiness` distinguishes `Implemented` from `ReferenceOnly/PartiallyImplemented`: an example showing `AvailableQuantities` must **not** claim present executable precheck when production still calls status directly.
- Validate length, unique group/workflow/step keys, referenced installed operation keys, documented major versions and the absence of control/write operation IDs; fail closed on malformed metadata.
- Project metadata through the **existing** API DTO (`AdapterCapabilityGroupResponse` or `AdapterOperationDocumentationResponse`), not a new endpoint. JSON schema/source compatibility and UI guards must remain.
- UI: in existing API & Examples tab show workflow sequence before individual typed invocation, plus note which steps are provider native, which are host-governed and which are planned. Keep no Connection picker or live calls. Screenreader-readable ordered steps, mobile wrap and no accidental raw credentials.
- First guide: ForeSite Surveillance applicability + status + LastGoodScanTime. Subsequent guide candidates: ForeSite trends, daily, cards; CygNet PNT/current/history/CAS; WAMI license/model/calc. Only publish installed operations; explicitly mark POC-only workflows.
- Testing: metadata projection parity, duplicate/wrong-operation/version/effect rejects, schema validation for typed examples, API authorization/no-source-call, mobile+desktop interaction/visual, no regression to existing tabs. No dotnet compile can be claimed without a capable environment.

## 5. Delivery steps — small PRs against latest develop

1. **Plan and traceability:** Save this plan in `docs/design/` when repo write access is available, link handoff references and scope issues, reconcile #502/#506 CI independently; do not overwrite POC evidence or existing #484 tracking.
2. **Contracts-only PR:** optional workflow-guide records, DTO mapping, validated declarations, tests; existing adapter registrations remain compatible (null/empty workflows).
3. **UI + first static ForeSite guide PR:** reusable renderer in existing tabs, correct explicit partial-readiness label, API/interaction/mobile tests, descriptive first ForeSite sequence. No live reads added.
4. **ForeSite discovery/current PR:** provider enum mapping, authorization-preserving catalog read, applicability/available/valid separation, one execution snapshot, units and scan semantics, targeted negative fixtures. Then update guide readiness to implemented with tests.
5. **ForeSite history + daily + card readiness PRs:** reuse #485, no duplicate history stack; historical data periods, completeness, unit/quality policy; resolve cards sample `lb`/`lbf` and Unknown quality contract before live hosted authoring claim.
6. **CygNet PNT catalog + native-read parity PRs:** native-source licensed POC port, allowlist and mapping, VHS statuses/limit reconciliation, CAS alarms, DDS passive setpoints later.
7. **WAMI staged PRs:** typed descriptor and fake/license checks, calculation functions and model artifact contract, opt-in factory/connection tester with explicit not-installed state, live Windows acceptance, then separately governed composition runtime and multi-provider V3.
8. **Well-test capability PR:** only after exact route and acceptance/status semantics are proven with fixture+live tests. Do not advertise rejected latest tests as usable values.
9. **Authoring later (separate design review):** role count/budget policy, graph/dependency ordering, authoring UX, simulation/fixture test harness, manifest V3 migration, output/parameter typing, model pinning, host-bound provider role execution, cross-provider time/units, revocation. Do not implement premature two-provider or WAMI-specific authoring UI.

### PR gates for every step
- Review current code and adjacent active PRs; smallest compatible change; no wholesale POC merge.
- Provider-neutral contract + provider-specific exact API call/verb; positive, negative, malformed, missing, stale/rejected/truncated and cross-source/revision cases.
- Preserve authorization/grant isolation and no provider fallback, `ReadOnly` effects and no browser preview.
- .NET tests, UI/API conformance, generated client typecheck, required CI on exact commit; visual tests when UI changes.
- Licensed native live verification documented distinctly; do not claim without test. Update operation status and docs only when implementation+tests justify it.

## 6. Three review-pass record

**Pass 1 — current framework:** Keep typed versioned descriptor / group / operation metadata and reusable UI. Missing ordered provider workflow metadata, readiness, authoring reference; no new execution framework.

**Pass 2 — POC evidence:** Added enum-ID applicability, per-provider units and timestamp semantics, consistency and cache scope, CygNet bounded historian caveats, WAMI model/version/session status, `Unknown` card quality and unit mismatch; rejected well tests.

**Pass 3 — authoring/execution scenarios:** Explicit period vs acquisition time, multi-input snapshot consistency, separate installed/applicable/valid/module-host-ready state; independent authorization and bounded roles for 1..N providers; preserve V1/V2 and defer V3 runtime/authoring. Also preserve rejection/test statuses, complete/truncated time series, versioned contracts and fail-closed negative tests.

### Status at save
Design reviewed, not merged into GitHub by this document. Stage 1 code changes require a writable repository environment and exact-head build/CI. This file is a handoff-ready implementation plan and may be stored in the user's Library and downloaded from this conversation.