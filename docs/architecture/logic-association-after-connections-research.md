# Logic association after Connections: findings and proposed first slices

Updated: 2026-10-20 after merged PRs #207–#221. Status: research and implementation proposal, not an approved control design or completed feature.

## Recommendation

Start with one approved .NET module, one imported ESP well, one ForeSite connection and one explicitly mapped numeric input. Prove a real Shadow execution before adding the write path. Then add one named ForeSite setpoint operation with recommendation approval, durable dispatch and verification. Add autonomous dispatch only after the same governed action path is proven. Repeat the pattern for CygNet using its own supported operations.

The owner's requested provider isolation is the right boundary: a ForeSite association reads and writes through ForeSite even when ForeSite uses CygNet underneath. A CygNet association reads and writes through CygNet. No automatic provider substitution. Modules consume inputs and emit intents through the SDK; the Gateway adapter owns vendor API calls. This preserves provider-specific behavior without embedding HTTP clients, vendor DTOs, identities or credentials in module code.

Hierarchy organizes Assets. It does not establish a measurement mapping, command destination, module compatibility, authorization inheritance or physical control ownership. These associations must be explicit.

## Reviewed snapshots and limits

| Source | Reviewed revision | Meaning |
| --- | --- | --- |
| [PR #205](https://github.com/abhee15/WellSiteAutoPilot/pull/205) | `ad04a9edf3cfaec72b5474e2db7616a35df31759` on `docs/connections-asset-discovery-20261019` | Draft simple-first design: runtime connections, adapters in this repo, discovery then Shadow; explicit owner merge hold |
| [Connections POC](https://github.com/abhee15/WellSiteAutoPilot/tree/e32d325d7bca31741323c5dac9198a21fa133121) | `e32d325d7bca31741323c5dac9198a21fa133121` on `poc/connections-205` | ForeSite and CygNet facility discovery, connection UI and hierarchy Apply; experimental code |
| Original research branch base | `59e062ab4a6a3362d6ba28fe3a84610134744923` | This branch adds this document only; it does not import the POC or #205 changes |
| Current develop review | `03d76b2cf9254ee10a9afe28a7e75fb2f4037d92` after #207–#221 | Authoritative implementation for the current findings below; POC observations are historical |

Findings below are static source inspection. No ForeSite/CygNet service, Windows delegation or equipment action was exercised in this research session. The POC's [findings](https://github.com/abhee15/WellSiteAutoPilot/blob/e32d325d7bca31741323c5dac9198a21fa133121/docs/poc/connections-205-findings.md) report a live ForeSite discovery/import of ten wells; those are the POC author's results, not a validation of telemetry or setpoint APIs. CygNet discovery was tested with recorded responses; native library execution on a CygNet installation remains unproven there.

## What the new merges resolve

The current implementation supersedes several POC gaps. Reuse it; do not build another connection store, source-binding table, importer or internal authentication mechanism.

| Merged PRs | Now present in develop | Consequence for the plan |
| --- | --- | --- |
| #208–#209 | Separate Asset type permissions, hide/restore/delete behavior | Use existing type management and neutral well compatibility; no administrator-only type prerequisite |
| #211 | Per-caller Gateway authentication and route policies; worker and Server credentials are distinct | Anonymous current-data access is closed. Extend the existing service identity boundary for execution/binding authorization |
| #212–#213 | Server-owned PostgreSQL connections, revisions, stale-test protection, dedicated `connections.read`/`connections.manage`, Administration UI | No JSON-file replacement work remains; association needs a safe read of existing connection/source references |
| #214 | Installer-selectable, remembered Gateway Windows account and reported acting identity | Service account configuration is implemented; per-person downstream enforcement/attribution is still separate |
| #215, #217 | Real ForeSite adapter check/discovery, declared grouping facts, complete/warnings/acting-as discovery response | Reuse its descriptor and mapping style; current telemetry and setpoints are still absent |
| #218–#220 | Reviewed fingerprint, fresh discovery, import UI, neutral `well`/`group`, unique source bindings and transactional Asset/group/binding import; referenced connection deletion blocked | Hierarchy/source identity groundwork exists. Connect it to Configured Logic instead of repeating import work |
| #221 | Permission-gated Status page and detailed system info; compact signed-in health response | Reuse status presentation; last manual connection test is not live data/control readiness |

The merged [workflow design](https://github.com/abhee15/WellSiteAutoPilot/blob/03d76b2cf9254ee10a9afe28a7e75fb2f4037d92/docs/design/connections-workflow-design.md), especially §§12–14, records the latest detail. Import currently requires deployment `connections.manage`, all-assets `assets.manage`, and `asset-types.manage` when a needed type is absent. Scoped import and selecting a destination parent are not implemented. Discovery/import are bounded at 5,000 objects. One physical object → one Asset with multiple source bindings is the agreed direction (D10); linking, CygNet implementation and check-for-changes remain later slices.

## What exists and what is missing

All current source paths below refer to [develop snapshot 03d76b2](https://github.com/abhee15/WellSiteAutoPilot/tree/03d76b2cf9254ee10a9afe28a7e75fb2f4037d92). The earlier CygNet agent remains POC-only evidence, not shipped develop functionality.

| Area and source | Observed behavior | Remaining gap relevant to logic association |
| --- | --- | --- |
| `Integration.ForeSite/ForeSiteAdapter.cs` | Check, well discovery, grouping facts, warnings and acting identity; advertises AssetDiscovery | No current UDC values, measurement catalog, setpoint operations, command status or readback |
| `IntegrationGateway/Adapters/AdapterCatalog.cs` | Simulator and ForeSite explicitly registered | No production CygNet adapter/agent in this snapshot; POC native discovery does not establish current reads or control |
| `Persistence/Connections/SourceBindingEntity.cs` | Durable ID, connection, resource kind, external ID, Asset ID, import snapshot and creation time; DB uniqueness/FKs | Configured Logic has no reference to this source binding; no binding revision, linking/reconciliation lifecycle or execution provenance yet |
| `Application/Connections/Importing/*`, `Persistence/Connections/ConnectionImportRepository.cs` | Fingerprint-reviewed fresh import; Assets/groups/bindings written in one serializable transaction; neutral types and duplicate-source protection | Connection-revision race and flat-import prerequisite below; type creation is performed separately before the Asset transaction |
| `Persistence/Connections/ConnectionRepository.cs` | Persisted revisions, blocked deletion while imports exist, audited setting-name changes | Update still permits changing a bound connection's `baseUrl`; source identity can change under existing bindings |
| `Domain/ConfiguredLogic/ConfiguredLogicDataBinding.cs` | Stores requirement, local Asset, `ProviderId`, external Asset ID and mapping JSON | No authoritative connection/source-binding reference; strings/JSON not verified against imported source |
| `Application/ConfiguredLogic/ConfiguredLogicService.cs` | Installed module, role/cardinality/type, parameters/schema and data mapping structure checks | No live measurement/operation validation or provider-family restriction; declared Asset traits are not enforced by `ValidateAssetBindingsAsync` |
| `Application/Executions/ConfiguredExecutionSnapshot.cs` | Carries provider and mapping JSON into immutable execution inputs | Add authoritative source/connection revision provenance and dispatch-time resolution |
| `Worker.DotNet/Execution/ModuleExecutionEngine.cs`, `GatewayDataClient.cs` | Quantity/unit, quality, age and future-time checks | `ProviderMappingJson` still not passed to the current read; selecting a UDC does not affect the call |
| `IntegrationGateway/Program.cs` | `/data/current` now requires authenticated Server/worker caller; router still registers only `simulator` | No saved-connection routing or execution/Asset/binding-specific authorization; raw external IDs remain accepted from those callers |
| `ModuleSdk/LogicModuleContracts.cs`, `BuiltInLogicModuleResolver.cs` | SDK inputs/results/intents exist; only sample module resolved | First real module needs explicit approved worker registration; catalog installation alone is not runtime availability |
| `ConfiguredLogicService.cs`, `ExecutionRequestV2Consumer.cs` | Shadow-only configured revisions and worker execution | Recommendation/Autonomous mode execution remains to implement |
| `RecommendationMaterializer.cs`, `RecommendationDecisionService.cs` | Recommendation-mode `ControlIntents` can materialize; approve/reject records a decision | Shadow does not create approval items; draft semantics/mode path need reconciliation; no durable command dispatch or verification |

### Source-inspection findings to address before relying on imports for logic

These are concrete code-path findings, not reproduced runtime failures in this session. They should receive focused regression tests before an implementation fix is merged.

1. **Bound connection address replacement remains allowed.** `ConnectionRepository.UpdateAuthorizedAsync` assigns `SettingsJson` without checking `SourceBindings`; `ConnectionService.UpdateAsync` validates settings but does not restrict replacement. After import, changing ForeSite `baseUrl` can make well ID 123 refer to a different installation. Reject source-changing settings on referenced connections, while allowing renames and enable/disable. Use a new connection and explicit rebinding for replacement initially, as the merged design §6 requires.
2. **Discovery/import does not pin the connection revision.** `ImportPlanner.Fingerprint` includes connection ID and planned fields, not connection revision. `ImportApplyRequest` carries no expected revision; `ApplyAuthorizedAsync` locks/rechecks enabled state but not the revision used by discovery. If the address changes after discovery and before the import transaction, the transaction can attach old discovery to the edited connection. Even separate preview/apply can accept a different source with identical projected IDs/fields. Carry the discovery connection revision through preview/fingerprint and apply; compare it under the locked connection row. Serializable isolation cannot validate a discovery that happened before the transaction. Fingerprint only covers placement/attributes, not every snapshot fact; this is not a complete source-identity or control-target attestation.
3. **First flat import unnecessarily requires a Group type.** Preview adds `group` to `TypesToCreate` only when `GroupCount > 0`. With empty levels and no existing `group`, `ImportAsync` creates only `well`, then unconditionally requires both types and returns `ASSET_TYPE_NOT_FOUND`. Make the Group type requirement conditional on an actual group. Test first import into an empty catalog with no levels as well as grouped import.
4. **Do not claim type creation is in the same transaction.** Missing types are created via `AssetService` before `ApplyAuthorizedAsync`. Failure of the later import can leave the types present even though no wells/bindings were created. This is distinct from atomic Asset/group/binding creation, which is implemented. Document the boundary or deliberately bring type creation into the transaction if full-operation atomicity is required.

Also retain existing limits: inactive bound Assets still count as imported; groups are reused by parent/name/type without bindings; linking and check-for-changes are not implemented. `PreviewAsync` deduplicates by external ID alone although binding identity includes resource kind; that works for today's single-kind ForeSite adapter but must change before a multi-kind provider can emit overlapping external IDs.

### Identity: implemented service account, remaining attribution decision

Server → Gateway now uses `WSA-Service` caller authentication, not Windows delegation. Gateway → ForeSite uses the Gateway's chosen Windows account. Tests/discovery display that account, and #214 makes its installer configuration persistent. This resolves how read-only calls identify the backend; do not propose replacing it for association work.

Per-person CygNet enforcement/attribution remains explicitly open in the merged design. The POC agent impersonates its immediate caller, which does not prove that the original browser user reaches CygNet across the chain. Confirm what identity ForeSite records/propagates for writes and which unattended identity is permitted. An AutoPilot audit username is not proof of provider-side human authorization. Never queue Windows tokens or user passwords.

## Minimal association model

Use the existing module catalog, Configured Logic revision lifecycle and execution snapshots. Add only what the first real module requires.

| Association | Proposed meaning |
| --- | --- |
| Asset source binding | Reuse `integration.source_bindings`: local Asset → connection + resource kind + external identity. Add authorized association lookup and references from configured inputs; do not create a second binding store |
| Module requirements | Named Asset role, supported well/device prerequisites, named engineering inputs, freshness/quality/unit rules, parameters and declared command requirements. An explicit provider-family restriction for the first provider-specific module is sufficient; a capability expression language is unnecessary |
| Configured input binding | Requirement ID + exact Asset → source binding + adapter-validated measurement key. Include UDC/device/group identifiers only where the actual provider contract needs them |
| Configured output binding | Command requirement ID + exact Asset → same permitted provider/connection + explicit operation/target, unit, limits and verification mapping. Do not infer a writable target from the input UDC |
| Execution/action provenance | Pin module version/hash, configuration revision, source-binding revision and connection revision; record actual input sample timestamps/quality and adapter support profile. The dispatcher rechecks current enabled state, authority and applicable compatibility before a call |

Distinguish adapter key (`foresite`), saved connection ID (one ForeSite instance), external well ID (only meaningful within that instance) and measurement key (one quantity/UDC on that well). Existing `ProviderId` can remain a transport detail, but must resolve to a persisted authorized connection rather than become a second routing authority.

For example, bind an illustrative ESP frequency input to local well W, source binding B on ForeSite connection F, and the measured-frequency mapping returned by that adapter. Bind its proposed frequency-setpoint output separately to F's supported write operation. The exact UDCs and operation names must come from the deployed ForeSite contract; no frequency mapping or limits are asserted here. Actual frequency, configured target frequency and last requested setpoint are different quantities.

Follow agreed D10: one physical well is one Asset with explicit ForeSite/CygNet source bindings; ForeSite is the well system of record. Linking is still unimplemented. Do not match/merge solely by display name or a non-qualified facility ID. Before enabling writes through both routes, resolve both bindings to the same physical control domain and allow one active write owner so two modules cannot independently command the device. Source linking does not authorize a ForeSite-only module to use CygNet or enable automatic fallback.

### Hierarchy and the operator journey

1. Open an imported leaf well and choose **Associate logic** using the existing Logic workspace.
2. Offer trusted executable module versions whose Asset type/traits, provider family and required operations fit. Show why an ESP module is unavailable when well/device classification is missing or incompatible. Reuse the now-imported neutral `well` type; validate its well/device classification and traits without loosening module requirements.
3. Resolve its persisted connection/source identity; present read mappings and, later, independently permitted output targets. Prefer adapter-provided validated choices to typing external IDs or JSON.
4. Enter parameters, check a bounded live input preview with source/timestamp/unit/quality, and save an immutable Draft. Review then activate Shadow explicitly. Saving or previewing must not activate a schedule or send a command.
5. Run once; inspect inputs, proposed outputs and failure reasons through existing execution detail. Add scheduling only after that path is proven; retain UTC/cadence conventions and explicit display timezone.
6. Enable recommendation/approved control only after the action path below exists. Explain whether approval merely records a decision or also requests dispatch.

Initially associate one well explicitly. Selecting a hierarchy group may later preview eligible descendant wells for a batch operation; freeze and authorize the exact reviewed set, with exclusions visible. No automatic attachment to future children, parent-scope inheritance or group-as-device routing. Non-well facilities can gain their own compatible modules later.

## ForeSite API evidence to obtain

The user's setpoint API observation is correct as a requirement. The reviewed repository contains no implementation or supplied contract establishing which ForeSite setpoint APIs are available. Public Weatherford research did not provide a ForeSite 5.7.1 operation contract or payloads. The table defines required semantics, not invented endpoint URLs or claims that these methods already exist. Inspect the actual supported API/service interfaces and the ForeSite setpoint UI's service calls with the product team.

| Needed capability | Evidence and behavior to verify | Needed by |
| --- | --- | --- |
| Well/device/data-connection details | Stable well ID, ESP classification, configured SCADA linkage, current device identity and relevant connectivity state | Association eligibility and target validation |
| Available measurements for a well | Supported engineering quantities/UDCs, provider mapping IDs, units, types, read permissions; determine whether arbitrary UDC access exists or only a fixed list | Mapping UI and adapter validation |
| Current measurement read | Exact selected value plus original sample time, quality and units; cached vs fresh/polled semantics, maximum latency, missing/unmapped behavior | Shadow input collection |
| Writable setpoint metadata | Allowed targets/operations per well/controller, data type, engineering units, limits, increment/precision and permission; separate observed value, configured value and device command | Output binding and preflight |
| Submit one setpoint | Actual operation and DTO; does it edit ForeSite configuration, write SCADA cache, publish a recommendation or send a field-device transaction? Does submission itself actuate? | Governed command adapter |
| Track request outcome | Request/transaction ID, queued/sent/rejected/completed states, asynchronous status, correlation/idempotency support, duplicate/timeout behavior | Durable dispatch and recovery |
| Verify result | Which read yields acknowledged/effective setpoint, sample timestamp and quality, tolerance, expected propagation delay; whether telemetry can prove device response | Verification |
| Identity and rights | Service authentication, well/Asset access filtering, operation rights, approver attribution, downstream CygNet behavior | All real usage; mandatory before writes |

For the first ESP example obtain sanitized request/response fixtures for one well, one input mapping, one writable target, one failed/denied request and one successful status/readback sequence. Validate the actual service account, not just an interactive developer. Confirm ForeSite's own optimization/control behavior will not compete with AutoPilot's chosen target.

If ForeSite exposes a typed measurement instead of arbitrary UDC lookup, map that supported measurement for the first module. If its existing API cannot send the required setpoint, record that as a provider/product gap for a supported ForeSite API addition or choose another expressly supported ForeSite operation. Do not silently call CygNet to complete a ForeSite association, scrape its UI at runtime or update its database directly.

## CygNet operations to investigate next

Public documentation establishes that point resolution and device commands are separate concepts. For UIS points, facility + UDC participates in resolution; other CVS types resolve differently [S1]. The .NET API provides Points access to PNT/CVS and Devices access to DDS/UIS, with local API help in `CygNet.API.chm` [S2]. Consequently FAC enumeration is useful discovery evidence but cannot establish a working current-data or command adapter.

For the chosen direct-agent path, inspect installed help/assemblies and prove a point lookup/current read with units, original time and status; then identify the supported device/setpoint transaction and status/readback operations. Retain domain, site/service, facility, point/UDC and any required data-group identity. A UDC alone is not a globally unique destination. The POC external facility string omits domain; ensure connection scope disambiguates it and reject a bound connection's scope replacement.

The documented scripting `SendSingleUdc` takes facility, data group, ordinal, UDC and value [S3]. This is evidence that writes may need more than a point ID, not a recommendation to implement COM or a claim of the same .NET method signature. Use the selected installed .NET contract and its tested permission/status behavior. A point/cache value update, recommendation-point publication and a device command have different effects; expose only the required, named operation.

The POC compiled a .NET 10 agent against 9.9 assemblies but did not prove native runtime loading on a CygNet installation. Older 9.8 documentation describes Framework prerequisites and architecture-specific assemblies [S2]. Treat the POC as useful evidence for a narrow agent, not blanket compatibility; resolve runtime/bitness against the actual installation before choosing deployment requirements.

## Proposed AutoPilot API/contract changes

Reuse existing configured-logic create/revise/validate/activate/Shadow and recommendation endpoints. Generate frontend clients from the server contract. These additions describe operations to design during implementation, not committed routes:

| Boundary | Small required addition |
| --- | --- |
| Server association queries | For an authorized Asset, return its source binding and eligible module/operation choices; avoid exposing administrator connection settings/secrets |
| Server mapping queries | List supported input/output choices for that exact bound resource; validate submitted choices against authoritative IDs and permissions |
| Server live preview/validation | Bounded read-only binding check; return value/unit/quality/time and clear unsupported, denied, stale, unavailable or missing mapping results; attach evidence to the checked revisions |
| Configured Logic revision | Persist input connection/source references and later declared output bindings/provider restriction; revalidate on activation and dispatch |
| Internal Gateway data call | Reuse implemented per-caller authentication; tie reads to execution + exact Asset/binding + operation; resolve connection and mapping authoritatively, preserve correlation, enforce least privilege and connection enabled state |
| Provider current-data interface | Pass the selected validated measurement mapping; extend the existing semantic contract only as needed. Reuse `EngineeringValue` for numeric input; require an actual source timestamp/quality |
| Server governed control | Atomically persist approved/eligible action and outbox dispatch; explicit policy, identity, expiry, binding revision and ownership checks |
| Internal Gateway command/verification | Execute one authorized persisted action through its bound connection; return provider receipt/status and verification evidence. Use existing command/verification contracts as a starting point, adding missing target/precondition fields only for demonstrated need |

A successful connection health check does not prove measurement access or command permission. Keep capability validation specific to the selected resource/operation. Do not turn the Gateway into an authenticated arbitrary-provider-URL proxy.

## Modes and the missing governed action path

Proposed behavior, subject to the outstanding control/identity decisions:

| Mode | Run and result | Provider mutation |
| --- | --- | --- |
| Shadow | Collect inputs, evaluate, persist proposed outcome | None; record hypothetical actions only |
| Recommendation | Evaluate and persist an expiring proposed action for review | Explicit approved action may proceed through policy and durable dispatch; approval is not evidence of completion |
| Autonomous | Evaluate and submit within configured permissions/limits and ownership | Same durable policy/dispatch/verification path, using agreed unattended identity; no per-action human approval |

Publishing recommendation values into ForeSite/CygNet is itself a provider write even if no device moves. Keep it a separate explicit capability from actuation, and confirm whether a downstream workflow owns approval/execution. Avoid both AutoPilot and a downstream system dispatching the same approved change.

Before writes, implement the following bounded controls for the first operation:

- Reconcile `RecommendationIntentDraft`, `ControlIntentDraft`, materialization and mode handling. A command must match a declared requirement and saved output binding; module output cannot introduce a new connection, target or operation.
- Persist action identity, configuration/module/binding revisions, exact target/value/unit, reason, input observations, requester/approver/execution identities, expiry and provider receipt. Use transactional state transitions/outbox and duplicate-safe dispatch.
- At approval and dispatch, recheck current AutoPilot/Asset/provider rights, module trust/license, connection enabled/compatibility and policy. Reject expired/superseded proposals or changed bindings. Do not reinterpret an old approval against a newly edited target.
- Check fresh relevant process values and setpoint preconditions, engineering bounds, maximum step, cadence/cooldown, equipment/local/manual status and ownership. Before-send reads reduce risk; only provider-supported conditional writes can close the race with an external operator.
- Serialize/fence competing actions for the same physical control domain across configurations. Existing same-configuration execution overlap checks are insufficient. A local ownership rule cannot prevent an unrelated external controller; coordinate its behavior explicitly.
- Acknowledge queued/sent/accepted separately from confirmed completion. On uncertain timeout/restart, reconcile using provider request IDs/status/readback; do not blindly retry. If the provider lacks idempotency/status, use a conservative attention-required outcome rather than claim exactly-once delivery.
- Verify against a fresh, good-quality readback with defined tolerance and bounded deadline. An unchanged cached value or HTTP 200 cannot prove physical effect. Report unresolved/failed outcomes; do not add an automatic rollback command without an operation-specific design.
- Disable blocks new dispatch, but cannot recall a sent action. Define stop/drain and in-flight reconciliation before writes. Reuse the proposed readiness direction after its status is resolved; no second readiness engine.

## Delivery sequence and completion evidence

This advances #205's simple-first sequence using the merged Connections/import foundation. It does not authorize merging #205 or the POC. Internal caller authentication, saved connections, discovery and import are implemented prerequisites, not new work to repeat.

| Slice | Deliverable | Completion evidence |
| --- | --- | --- |
| 1. Connect existing bindings to logic | Address source replacement/revision and flat-import findings; authorized Asset/source-binding lookup, configured input references and saved-connection current-data routing | Reuse DB connections, bindings and service auth; prove two same-adapter connections cannot cross-route; denied/disabled/stale bindings fail; grouped and first flat import work |
| 2. First ForeSite module in Shadow | One explicitly registered approved .NET module, one real ESP well, one mapped input, association UI and live preview | Actual service identity reads correct quantity/UDC, unit/time/quality; manual run persists observations/proposal with zero writes; stale/bad/missing/unit mismatch fail clearly |
| 3. Recommendation without actuation | Deliberate mode support, materialized expiring proposals, approve/reject audit and consistent UI wording | Module intents stay within bindings; out-of-scope, changed/expired/replayed decisions are tested; no command until action dispatch slice exists |
| 4. One approved ForeSite setpoint | Inspected provider contract, durable action, policy/ownership, approval-to-dispatch and status/readback | Controlled test environment proves target and units, one logical action under duplicate delivery/restart, denied permission, timeout uncertainty and delayed readback behavior |
| 5. Autonomous for that operation | Same action path with agreed identity and bounded policy | Scheduled trigger, revocation, disable/stop, conflicting controller, cooldown and recovery tests pass; operator can understand current ownership and outcome |
| 6. CygNet equivalent | Proven native current-data operation, explicit mapping and later one governed write | Live native-library/identity validation plus the same read/intent/dispatch contract tests; ForeSite associations never touch this route |

First module authoring: implement `ILogicModuleV1` as a deterministic input/parameter-to-result function; declare requirements/parameters/commands in its manifest; add unit tests with good, stale, bad-quality and boundary values; explicitly register that approved module in the initial .NET worker. Package/hash/trust approval and worker executability are separate checks. Do not build general dynamic code loading or Python execution to prove this first slice. If a future real algorithm needs historical windows, persistent state, calculations or multiple inputs, implement that concrete dependency before claiming it supported; current worker input access supports Current only and its numeric engineering-value shape is not a universal point type.

For integration acceptance, use PostgreSQL + messaging + Server + worker + Gateway and provider fixtures to verify provenance, bindings, denied/cross-connection access and zero-write Shadow behavior. Keep live Windows/ForeSite/CygNet evidence separate from fixture results. Required current-head repository CI remains a merge gate for implementation PRs.

## Questions to settle with provider evidence

1. Which exact ESP algorithm and input/output should be the first vertical slice? Pick a supported observable numeric operation before generalizing the manifest or adapter.
2. What ForeSite API maps a well to its available UDCs/typed measurements, and which retrieves sample time/quality with the value? Is fresh polling required or is bounded cached telemetry sufficient?
3. Which ForeSite operation actually submits the intended ESP setpoint, with what target identifiers, units, limits, acknowledgement and readback? Does it provide a transaction/idempotency key?
4. Does AutoPilot own approval/execution or publish to an existing ForeSite/CygNet workflow? Which system is the sole dispatcher, and how is completion reported back?
5. What Windows identities and per-person enforcement/attribution are required for manual, approved and autonomous actions across all hops?
6. Which deployed controller/device/SCADA configuration and existing optimization loops must the first module cooperate with?

Answers affect operational writes. They need not stop connecting the existing source bindings to authorized current reads and a clearly read-only Shadow slice once its own API evidence is available.

## External sources and evidence limits

Public primary sources were checked on 2026-10-20. They ground CygNet concepts, not a ForeSite API specification or proof of installed-version behavior.

- **[S1]** [CygNet Points Overview, 9.6](https://softwaredocs.weatherford.com/cygnet/96/Content/Topics/Points/Points%20Overview.htm): CVS-dependent resolution, sample point semantics and multiple data types. Used here only for the distinction between facility, point and device target.
- **[S2]** [CygNet .NET API, 9.8](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/Scripting/CygNet%20.NET%20API.htm): Points/PNT/CVS and Devices/DDS/UIS responsibilities, architecture-specific assemblies and installed help. Does not establish .NET 10 runtime support for the POC's 9.9 assemblies.
- **[S3]** [CygNet UisClient Methods, 9.5](https://softwaredocs.weatherford.com/cygnet/95/content/topics/Scripting/CxUis%20UisClient%20Methods.htm): scripting send arguments. Used only as evidence of operation-specific targeting; not a selected implementation contract.

Repository source inspection at `03d76b2cf9254ee10a9afe28a7e75fb2f4037d92` is the evidence for the updated implementation gaps above. No application code or migration is changed by this research update; runtime regressions above are proposed acceptance tests, not claims of executed tests. No actual ForeSite telemetry/setpoint API names or payloads have been verified by this research. Retain #205's draft/unmerged owner-review hold; this research is a separate docs-only proposal.
