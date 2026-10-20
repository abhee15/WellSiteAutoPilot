# Logic association after Connections: findings and proposed first slices

Date: 2026-10-20. Status: research and implementation proposal, not an approved control design or completed feature.

## Recommendation

Start with one approved .NET module, one imported ESP well, one ForeSite connection and one explicitly mapped numeric input. Prove a real Shadow execution before adding the write path. Then add one named ForeSite setpoint operation with recommendation approval, durable dispatch and verification. Add autonomous dispatch only after the same governed action path is proven. Repeat the pattern for CygNet using its own supported operations.

The owner's requested provider isolation is the right boundary: a ForeSite association reads and writes through ForeSite even when ForeSite uses CygNet underneath. A CygNet association reads and writes through CygNet. No automatic provider substitution. Modules consume inputs and emit intents through the SDK; the Gateway adapter owns vendor API calls. This preserves provider-specific behavior without embedding HTTP clients, vendor DTOs, identities or credentials in module code.

Hierarchy organizes Assets. It does not establish a measurement mapping, command destination, module compatibility, authorization inheritance or physical control ownership. These associations must be explicit.

## Reviewed snapshots and limits

| Source | Reviewed revision | Meaning |
| --- | --- | --- |
| [PR #205](https://github.com/abhee15/WellSiteAutoPilot/pull/205) | `ad04a9edf3cfaec72b5474e2db7616a35df31759` on `docs/connections-asset-discovery-20261019` | Draft simple-first design: runtime connections, adapters in this repo, discovery then Shadow; explicit owner merge hold |
| [Connections POC](https://github.com/abhee15/WellSiteAutoPilot/tree/e32d325d7bca31741323c5dac9198a21fa133121) | `e32d325d7bca31741323c5dac9198a21fa133121` on `poc/connections-205` | ForeSite and CygNet facility discovery, connection UI and hierarchy Apply; experimental code |
| Research branch base | `59e062ab4a6a3362d6ba28fe3a84610134744923` (`develop` at review) | This branch adds this document only; it does not import either branch's changes |

Findings below are static source inspection. No ForeSite/CygNet service, Windows delegation or equipment action was exercised in this research session. The POC's [findings](https://github.com/abhee15/WellSiteAutoPilot/blob/e32d325d7bca31741323c5dac9198a21fa133121/docs/poc/connections-205-findings.md) report a live ForeSite discovery/import of ten wells; those are the POC author's results, not a validation of telemetry or setpoint APIs. CygNet discovery was tested with recorded responses; native library execution on a CygNet installation remains unproven there.

## What exists and what is missing

All source paths in this table refer to the POC snapshot above unless stated otherwise. Existing components should be extended rather than introducing a second module/binding/workflow framework.

| Area and source | Observed behavior | Gap relevant to logic association |
| --- | --- | --- |
| `Integration.ForeSite/ForeSiteAssetDiscoveryProvider.cs` | Implements discovery, health and metadata; calls `Wells/All` and `SurfaceNetworkService/Asset/All`; advertises only discovery | No current UDC values, measurement catalog, setpoint operations, command status or readback implementation |
| `Integration.CygNet/CygNetFacilityDiscoveryProvider.cs`, `CygNetAgent/Program.cs` | FAC facility enumeration; agent exposes health and facilities | FAC discovery does not resolve current points, DDS/device transactions or commands; discovery partial-permission flag is dropped |
| `Server/Connections/ConnectionEndpoints.cs` | Imports source metadata plus `connectionId` and `externalId` into Asset JSON | No dedicated source-binding integrity; CygNet imports also receive `foresite-well`/`foresite-group` types; provider resource kind and domain must remain unambiguous |
| Same Apply endpoint | Rediscovers at Apply; loops over all not-yet-imported results, scans up to 500 existing leaves/groups | Does not apply an immutable reviewed selection; concurrent imports and inventory above 500 require DB-backed uniqueness and bounded complete lookup; partial Apply can leave partial imports |
| `IntegrationGateway/Connections/Connections.cs` | Builds discovery providers per saved connection; stores connection JSON on disk | Runtime data router is separate; endpoint/settings edits and deletion are not protected by persisted references; connection storage belongs in the planned Server DB model |
| `Domain/ConfiguredLogic/ConfiguredLogicDataBinding.cs` | Stores requirement, local Asset, `ProviderId`, external Asset ID and mapping JSON | No explicit durable connection/source binding identity; caller-supplied strings/JSON are not verified against the imported source |
| `Application/ConfiguredLogic/ConfiguredLogicService.cs` | Validates installed module, roles/counts/type, parameters/schema, required data mappings and JSON structure | No live operation compatibility or point existence checks; no enforcement that all bindings of a ForeSite-only module remain ForeSite; declared Asset traits are not checked by `ValidateAssetBindingsAsync` |
| `Application/Executions/ConfiguredExecutionSnapshot.cs` | Carries provider and mapping JSON into immutable execution inputs | Useful foundation; add binding/connection revision provenance and dispatch-time authoritative resolution |
| `Worker.DotNet/Execution/ModuleExecutionEngine.cs`, `GatewayDataClient.cs` | Reads by provider ID + external ID + quantity; enforces quantity/unit, quality, age and future timestamp rules | `ProviderMappingJson` is not consumed/passed to the Gateway read; a selected UDC cannot affect the request |
| `IntegrationGateway/Program.cs` | Current-data router registers only `simulator`; `/data/current` accepts raw external identifiers | Saved real connections never enter this route; old data routes remain outside the POC's secured connection group; require internal authentication and exact Asset/operation authorization |
| `ModuleSdk/LogicModuleContracts.cs` | Inputs, engineering results, recommendation drafts, control intents and state updates are represented | Reuse this model; intents must be checked against declared command requirements and persisted output bindings before any dispatch |
| `Worker.DotNet/Execution/BuiltInLogicModuleResolver.cs` | Resolves only `SampleShadowReadModule` | Catalog/package registration does not make arbitrary payload executable; first module needs explicit worker registration or a separately proven approved-package runtime |
| `ConfiguredLogicService.cs`, `ExecutionRequestV2Consumer.cs` | Configured revisions and worker execution are Shadow-only | Mode names elsewhere are not functioning Recommendation/Autonomous execution support |
| `Application/Recommendations/RecommendationMaterializer.cs` | Materializes `ControlIntents` only for completed Recommendation-mode executions | Shadow results do not automatically create approval items; `RecommendationIntentDraft` is not the materializer's source; mode/output semantics need one consistent path |
| `RecommendationDecisionService.cs`, `Messaging.Contracts/Control/ControlMessages.cs` | Decision persistence and command message DTOs exist | Approval does not create/send a command; durable ControlAction policy, ownership, dispatch and verification are missing, also stated in `docs/development/roadmap.md` |

### Identity is still an actual gap

The Server-to-Gateway client and the adapter's HTTP handler use `UseDefaultCredentials`. The agent impersonates its immediate authenticated caller. Together these do not demonstrate that the original browser user reaches CygNet: the immediate caller can be the Gateway service account. Agent `/health` does not use the facilities route's impersonation path either. Test the exact Server → Gateway → provider/agent → CygNet chain under the intended Windows service identities; document reachability, AutoPilot permission, provider permission and human attribution separately.

Keep the unresolved per-person CygNet enforcement/attribution requirement visible for ForeSite-mediated control too. Confirm what identity ForeSite records and propagates downstream. An AutoPilot audit username does not prove CygNet enforced that person's rights. Scheduled/autonomous actions need an explicitly agreed unattended identity. Never put Windows tokens or user passwords in queued messages.

## Minimal association model

Use the existing module catalog, Configured Logic revision lifecycle and execution snapshots. Add only what the first real module requires.

| Association | Proposed meaning |
| --- | --- |
| Asset source binding | Local Asset ID → connection ID + provider resource kind + immutable external resource identity. Store in an authoritative model with uniqueness/reference checks, not editable free-form Asset metadata alone |
| Module requirements | Named Asset role, supported well/device prerequisites, named engineering inputs, freshness/quality/unit rules, parameters and declared command requirements. An explicit provider-family restriction for the first provider-specific module is sufficient; a capability expression language is unnecessary |
| Configured input binding | Requirement ID + exact Asset → source binding + adapter-validated measurement key. Include UDC/device/group identifiers only where the actual provider contract needs them |
| Configured output binding | Command requirement ID + exact Asset → same permitted provider/connection + explicit operation/target, unit, limits and verification mapping. Do not infer a writable target from the input UDC |
| Execution/action provenance | Pin module version/hash, configuration revision, source-binding revision and connection revision; record actual input sample timestamps/quality and adapter support profile. The dispatcher rechecks current enabled state, authority and applicable compatibility before a call |

Distinguish adapter key (`foresite`), saved connection ID (one ForeSite instance), external well ID (only meaningful within that instance) and measurement key (one quantity/UDC on that well). Existing `ProviderId` can remain a transport detail, but must resolve to a persisted authorized connection rather than become a second routing authority.

For example, bind an illustrative ESP frequency input to local well W, source binding B on ForeSite connection F, and the measured-frequency mapping returned by that adapter. Bind its proposed frequency-setpoint output separately to F's supported write operation. The exact UDCs and operation names must come from the deployed ForeSite contract; no frequency mapping or limits are asserted here. Actual frequency, configured target frequency and last requested setpoint are different quantities.

If the same physical well is imported through ForeSite and CygNet, do not merge by display name. Before enabling writes to it through both routes, establish an explicit shared physical control-domain identity and ownership rule so two modules cannot independently command the same device. Initially allow one active write owner for that control domain; no general cross-provider matching engine is needed.

### Hierarchy and the operator journey

1. Open an imported leaf well and choose **Associate logic** using the existing Logic workspace.
2. Offer trusted executable module versions whose Asset type/traits, provider family and required operations fit. Show why an ESP module is unavailable when well/device classification is missing or incompatible. Normalize the POC's provider-named Asset types deliberately; do not loosen type validation to make the sample fit.
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
| Internal Gateway data call | Authenticated request tied to execution + exact Asset/binding + operation; resolve connection and mapping authoritatively, preserve correlation, enforce least privilege and connection enabled state |
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

This extends #205's simple-first sequence after the active authentication work. It does not authorize merging #205 or the POC or changing their implementation.

| Slice | Deliverable | Completion evidence |
| --- | --- | --- |
| 1. Source bindings and secure routing | DB-backed named connections/source binding references; provider-neutral Asset typing; authenticated authorized real current-data route | Two connections of the same adapter cannot cross-route; raw identifier substitution/denied/disabled cases fail; reference and stale-revision checks hold |
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

Answers affect operational writes. They need not stop source-binding cleanup, authorized current reads and a clearly read-only Shadow slice once its own API evidence is available.

## External sources and evidence limits

Public primary sources were checked on 2026-10-20. They ground CygNet concepts, not a ForeSite API specification or proof of installed-version behavior.

- **[S1]** [CygNet Points Overview, 9.6](https://softwaredocs.weatherford.com/cygnet/96/Content/Topics/Points/Points%20Overview.htm): CVS-dependent resolution, sample point semantics and multiple data types. Used here only for the distinction between facility, point and device target.
- **[S2]** [CygNet .NET API, 9.8](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/Scripting/CygNet%20.NET%20API.htm): Points/PNT/CVS and Devices/DDS/UIS responsibilities, architecture-specific assemblies and installed help. Does not establish .NET 10 runtime support for the POC's 9.9 assemblies.
- **[S3]** [CygNet UisClient Methods, 9.5](https://softwaredocs.weatherford.com/cygnet/95/content/topics/Scripting/CxUis%20UisClient%20Methods.htm): scripting send arguments. Used only as evidence of operation-specific targeting; not a selected implementation contract.

Repository source inspection is the evidence for the implementation gaps above. No actual ForeSite telemetry/setpoint API names or payloads have been verified by this research. Retain #205's draft/unmerged owner-review hold; this research is a separate docs-only proposal.
