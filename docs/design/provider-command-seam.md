# Provider command seam (shared design for ForeSite setpoints and CygNet commands)

Status: **proposed, for review. No code accompanies this document and no write has been run against any provider.**
It is the shared design that master plan item L1 and gap 20 call for. Gaps 21 (ForeSite) and 22 (CygNet) build on it
and stay blocked until it is reviewed.

It extends [autonomous-real-sink-readiness.md](autonomous-real-sink-readiness.md), which is a read-only survey of the
ForeSite source and a safety checklist. That document predates the readback and ambiguous-action work now on `develop`,
so section 2 below restates what is built, and section 8 lists what in the survey is superseded.

## 1. Goal and non-goals

A module's control intent (an approved recommendation, or an Autonomous run) can end in a change at a provider:
a ForeSite setpoint, or a CygNet command that adjusts a controller registry value. The change must be bounded,
attributable, verified against the equipment, and impossible to repeat by accident.

Non-goals: enabling real writes in any build (they stay disabled until section 9's go/no-go); direct CygNet point
writes (not verified to exist, see 6.2); changes to ForeSite or CygNet products (ADR-005: integrate through existing
supported interfaces, and record anything missing as an explicit integration gap).

## 2. What exists today (verified against `develop`)

| Piece | State |
| --- | --- |
| Gate (`AutonomousControlService`) | Kill switch, deployment switch, per-configuration policy (allowlist, unit, inclusive bounds, rate limit), expiry and staleness, one `ControlAction` per recommendation claimed atomically, kill switch re-read after the claim. |
| Sink port (`IControlCommandSink`) | `DispatchAsync` (idempotent per key) and `ReadbackAsync` (`Applied` / `NotApplied` / `Unknown`; a sink must never guess). |
| Outcomes | A sink exception or cancellation is **Ambiguous**, never Failed. Accepted is **Dispatched**, then one immediate readback: match is Verified, mismatch is Ambiguous, Unknown leaves it Dispatched. Ambiguous and Verified count toward the rate limit. |
| Operator tools | `POST /control/actions/{id}/verify` and `/resolve` (attestation with a required note), now with a web panel. |
| Sinks | `SimulatedControlCommandSink` (with fault and readback behaviours for tests) and `RefusingProviderControlCommandSink` (always `REAL_PROVIDER_WRITES_DISABLED`). `SetPolicyAsync` accepts only the Simulator sink. |
| Provider contracts | `ICommandProvider`, `IVerificationProvider`, `ProviderCommandRequest/Result`, `ProviderVerificationRequest/Result`, and capability kinds `Command` and `Verification` exist in `Integration.Contracts` but no adapter implements them. |
| Gateway | Callers `server` and `worker`; read routes per adapter (`/adapters/cygnet/current`, `/adapters/foresite/current`), all server-only except worker current-data reads. No command route. |
| Simulator | `ProviderSimulator` is exercised through the Gateway in CI ("Provider simulator through Gateway"). It has no command surface. |

Two gaps in the existing flow matter once a sink is real. Both are things a simulator hides:

1. **Immediate readback assumes instant effect.** After `Dispatched`, the gate reads back once at once. A real device takes
   seconds to minutes, so a correct write would usually read `NotApplied` and be marked **Ambiguous** prematurely, filling the
   operator queue with false alarms. Verification needs a settle window and a deadline (4.5).
2. **Nothing settles an action stuck in `Requested`.** If the Server stops between the claim and the sink call finishing, the
   action stays `Requested` and nobody is told. For a command that may have been sent this must become Ambiguous after a
   bound (4.6).

## 3. Principles

1. **Server decides, Gateway executes.** The Gateway never chooses what to write; the Server never speaks to a provider.
2. **A target is resolved from configuration, never from module output.** A module names a command; the platform maps it.
3. **At most one send per `ControlAction`, ever.** No retry, anywhere in the path, of a send whose result is unknown.
4. **"Sent" is not "applied".** Only a read of the equipment, by a path that cannot itself move it, verifies a write.
5. **Fail closed and fail loud.** Anything unexpected is Ambiguous or refused, audited, and visible in the operator panel.
6. **Opt in at every layer:** deployment, connection, command, policy, and (for a person approving) the recommendation.
7. **Nothing provider-specific in the core** (ADR-005, `provider-adapter-contracts.md`).

## 4. Design

### 4.1 Shape

```text
Gate (Server) -> GatewayControlCommandSink -> Gateway (server caller only) -> adapter command provider -> ForeSite / CygNet
                      |                                                                 ^
                      +-- readback: same Gateway, read-only measurement path -----------+
```

- A `GatewayControlCommandSink` (Kind `Provider`) is the only new Server class. It resolves the **target** (4.2), calls the
  Gateway, and maps the answer onto the existing outcome model. The gate does not change shape.
- The Gateway gets `POST /adapters/{adapterKey}/commands` and `POST /adapters/{adapterKey}/commands/verify`, authorised for
  the **server caller only** (the worker policy must not reach them), following the existing
  `ConnectionMeasurementRequest` pattern: the request names a connection, and the Gateway loads that connection's settings
  and secret itself.
- Each adapter implements `ICommandProvider` / `IVerificationProvider` and advertises `Command` / `Verification` in its
  descriptor only when it truly supports them.
- **Read and write are separate capabilities with separate credentials.** Having read access to a connection never implies
  write access.

### 4.2 Command catalog (the mapping from intent to provider action)

A per-connection **command catalog**, edited by people with the right permission and versioned, is the only way a command
name reaches a provider. An entry holds:

- `name` (what modules and policies call it), `quantity` and `unit`;
- **hard engineering limits** (`min`, `max`, optionally `maxStep`) that no policy may widen. A policy's bounds must lie inside
  the entry's, checked on save and again by the gate at dispatch;
- the **provider target** as an adapter-owned, schema-validated blob (ForeSite: well data group and setpoint element;
  CygNet: UIS command name and the registry parameter), never free text from a module;
- the **verification spec**: which read-only measurement confirms the write, tolerance, settle time, deadline;
- `enabled`, so one command can be switched off without touching the rest.

The catalog is configuration in the sense of ADR-005 (point and command mappings are explicitly allowed). A catalog entry
is not created by a module package and a module cannot name a target.

### 4.3 Authority

- A connection has `commandsEnabled` (default off) and, separately from its read identity, a **write identity** that is
  least-privilege: ForeSite `IssueWellCommands` for allowlisted wells and the CygNet access level for only the needed UIS
  commands (gap 17). Missing write identity means the Gateway refuses.
- Enabling `commandsEnabled`, adding a catalog entry, or widening its limits is a deliberate administrative act with its own
  audit record. Phase 2 makes it two-person (7.2).
- The deployment switch, the kill switch (checked again after the claim) and the policy keep applying unchanged. The kill
  switch store being unreachable must refuse dispatch rather than assume "released".

### 4.4 Dispatch contract and outcome mapping

The Gateway answers with a **closed set**, not free text. The sink maps it onto statuses that already exist:

| Gateway answer | Meaning | Action status |
| --- | --- | --- |
| `Sent` | The provider accepted the command (ForeSite `"success"`, UIS `true`). Proves nothing about the equipment. | Dispatched |
| `Rejected` + code (`FORBIDDEN`, `LICENSE`, `COMMAND_NOT_FOUND`, `NOT_WRITABLE`, `INVALID_TARGET`) | The provider **did not** act, and the reason will not change on retry. | Failed, with a distinct non-retryable gate code |
| Timeout, transport error, malformed or unrecognised reply, cancellation | The command may have been sent. | Ambiguous (already the behaviour) |
| `DryRun` | Validated and recorded, nothing sent (4.7). | Dispatched with sink `DryRun` until the DryRun status exists |

Rules: a provider status string other than the exact documented success is never treated as success; a provider reply is
stored (bounded, redacted) on the action for audit; the HTTP client on the command route has **retries disabled**, and a
test fails if a resilience handler is ever attached to it.

### 4.5 Verification: settle, then decide

Replace "one immediate readback" with a bounded verification state machine, driven by the catalog entry:

1. After `Sent`, record `verifyAfter = sentAt + settle` and `deadline = sentAt + deadline`.
2. A hosted service in the Server (single-flight per action, claimed by compare-and-set like `TransitionAsync`) reads back
   once `verifyAfter` has passed, then at a bounded interval until the deadline.
3. `Applied` and equal within tolerance is **Verified**. Before the deadline, `NotApplied` and `Unknown` leave the action
   **Dispatched**. A mismatch is **Ambiguous** at once (the device holds a different value than commanded). At the deadline
   an unverified action becomes **Ambiguous**, and an operator resolves it with the existing tools.
4. The readback reads a value by the **read-only measurement path**. ForeSite's `GetSetPointsParamsForSingleWell` is itself a
   device command and is **not** an acceptable automatic readback. The DDS token confirmation may be recorded as "delivery
   confirmed" evidence alongside the value, never instead of it.

Operator verify and resolve keep working at any point and are the manual path for anything the service gave up on.

### 4.6 Stuck `Requested`

A sweeper in the same hosted service moves any `Requested` action older than a bound (default two minutes, always greater
than the sink timeout) to **Ambiguous** with a dedicated gate code, once, by compare-and-set. It never re-sends. This is
the only place a restart can leave a command in doubt, and it is the reason the single-send rule can be audited.

### 4.7 Dry-run sink

Dry-run exists to prove a policy would behave before any write. A `DryRun` sink kind reads the current value through the
read-only path, records the would-be write, sends nothing, and returns a distinguishable result. It is **not** on `develop`
today (it was in the earlier prototype only). Decisions: it is selected **per policy** through the existing `SinkKind`
field, not by a global flag, so one deployment can dry-run one configuration; the recommendation shows a distinct status
rather than Executed (resolving open question 9 in the readiness document); and a `Provider` policy may require a recorded
dry-run history first (an owner decision, section 9).

### 4.8 Gate additions and phasing

| Phase | Added before any real write | Why |
| --- | --- | --- |
| **0: minimum** | Catalog limits as a hard ceiling; connection and command opt-in; policy sink `Provider` allowed only with both; single-writer rule per setpoint; no retry on the command path; settle-and-deadline verification; stuck-`Requested` sweeper; kill-switch-unreachable fails closed; every attempt audited | Without these a write is unbounded, repeatable or invisible. |
| **1** | Interlocks (alarm, shut-in, local override, stale comms, bad quality, stale inputs, command already in flight), change windows and blackouts, maximum step and daily drift against a baseline, first-N human confirmation tier | Safety rules that depend on site knowledge and provider data. |
| **2** | Dual control for enabling and widening, a dedicated permission split from `control.manage`, automatic revert of a mismatch (one gated attempt), a circuit breaker that opens a policy after N unverified results | Operational maturity; each needs an owner decision. |

### 4.9 Audit

Every attempt, including dry-run, blocked and refused, stores: actor or service identity, connection and catalog entry
(with version), policy snapshot hash, prior value (a read-only read before the write), requested value, the provider's bounded
reply and token, the readback value, and the correlation to the recommendation and execution. New `ControlAction` columns
for connection, catalog entry, prior value and provider token; the provider reply stays bounded. Reconcile against
ForeSite's own `SetPoints` audit rows where they exist.

## 5. Contract changes

- `ProviderCommandRequest` gains the connection id, the catalog entry reference (name, version, resolved target) and the
  token; `ProviderCommandResult.Status` becomes a closed enum plus code instead of a string.
- `ProviderVerificationRequest/Result` carry tolerance and settle information and the observed value as an
  `EngineeringValue`.
- `ControlDispatchResult(bool Accepted, ...)` is widened to the closed answer in 4.4; `ControlSinkKinds` gains `DryRun`.
- `ControlActionStatus` is unchanged in phase 0. A distinct `DryRun` presentation is added with the dry-run sink.
- The web client continues to be generated from OpenAPI; new Gateway routes are internal and do not appear in the public
  contract.

## 6. Provider specifics

### 6.1 ForeSite (gap 21, first integration)

Known from source (see the readiness document): setpoints go through ForeSite's own operations with permission
(`IssueWellCommands`), licence, bounds, history and audit; UIS send returns bool-or-null; confirmation is a DDS transaction
matched by a token; the status is a string where only exact success counts. **Not known:** whether the callable surface the
existing `ForeSiteSession` uses can reach the setpoint operation, how a service principal maps to a ForeSite user, and
whether `AutonomousControl` is licensed at a target site. **Probe P1** (read-only first, then one write on a test well)
answers these before any adapter code is written. A single-writer rule with ForeSite's own idle-time optimiser is needed
for any shared setpoint.

### 6.2 CygNet (gap 22, after ForeSite)

UIS commands confirmed by data-group transaction are the supported route. A direct point-write API was not found in WSA and
was not inspected in the CygNet libraries; **Probe P2** establishes whether one exists and what authorisation it enforces.
Until then CygNet writes are an explicit integration gap, not an assumption (ADR-005). Writing through CygNet bypasses
ForeSite's licence, bounds and audit, so where ForeSite manages the same well, ForeSite is preferred.

## 7. Guard rails to build with it

1. **Authorisation inventory:** the API-authorisation CI check already pins every Server endpoint. The Gateway command
   routes get the same treatment: a test that enumerates Gateway routes and fails if a command route is reachable by the
   `worker` caller or is missing a policy.
2. **At-most-once test:** a gate test with a counting fake sink asserts one `DispatchAsync` per action across retries of the
   HTTP request, duplicate execute calls and a restart between claim and dispatch.
3. **Fault matrix through the real Gateway:** extend `ProviderSimulator` with a command surface and the same faults the
   in-process simulator has (fail before applying, fail after applying, slow apply, wrong value, unknown). The existing CI
   job "Provider simulator through Gateway" then covers the whole path with no real provider.
4. **No-retry guard:** a test fails if the command-route client is built with a retrying handler.
5. **Catalog ceiling:** property tests that no policy accepted by `SetPolicyAsync` can exceed its catalog entry, and that the
   gate re-checks it at dispatch.
6. **Web copy pinned to server codes:** the existing test that keeps the gate-message table equal to `GateCodes` will cover
   the new codes; the Execute and Resolve copy that says "only the simulator can be commanded" must change in the same pull
   request that first allows a `Provider` policy, and a test should fail if it does not.

## 8. What this supersedes in the readiness document

- Item 9 (ambiguity handling and operator resolve) and the "operator resolve API" checklist entry are built.
- The proposed tri-state outcome with a new `Unverified` status is not needed: exception-as-Ambiguous plus the deadline in 4.5
  covers it with the statuses that exist.
- The DryRun sink described as implemented is not on `develop`; 4.7 re-specifies it.
- The "do not hold the claim open during a long wait" point is satisfied by dispatching once and verifying later (4.5).

## 9. Decisions needed from the owner

1. **ForeSite as the sanctioned write path**, with raw CygNet writes out of scope until P2. (Recommended: yes.)
2. **Pilot scope:** which wells, setpoints and device types; engineering min, max and step for each catalog entry.
3. **Identity of record** for a machine-issued command: the ForeSite user the service maps to, and audit or regulatory
   expectations.
4. **Single-writer rule** with ForeSite's own autonomous idle-time optimiser for any shared setpoint.
5. **Verification standard:** value readback with tolerance (recommended) versus DDS confirmation only; settle time,
   deadline, and whether a mismatch halts the policy or only alerts.
6. **Dry-run soak:** is a recorded dry-run history (and how long) required before a `Provider` policy can be saved?
7. **Permission split:** introduce a dedicated permission, and two-person approval, before the first real write or after the
   pilot?
8. **Licensing:** is `AutonomousControl` licensed at the pilot site, and does WSA-initiated control need its own flag?
9. **Where the write host runs** and whether the UIS call may run from the Gateway's service identity.

## 10. Delivery plan

Small steps, each merged with green CI, and none enables a real write:

| Step | Delivers | Needs |
| --- | --- | --- |
| S1 | Command catalog and connection opt-in: domain, persistence, API, read-only view. No dispatch. | Decisions 2, 3 |
| S2 | Provider simulator command surface and Gateway command and verify routes (server-only), with the fault matrix and route-authorisation guard. | S1 |
| S3 | `GatewayControlCommandSink`, closed outcome mapping, settle-and-deadline verification service, stuck-`Requested` sweeper, no-retry and at-most-once tests. `Provider` policy still refused outside tests. | S2, decision 5 |
| S4 | Dry-run sink and distinct status; soak reporting in the panel. | S3, decision 6 |
| P1 | ForeSite probe on a test well (read-only, then one supervised write). | ForeSite access, decisions 1, 8 |
| S5 | ForeSite adapter command and verification providers; first `Provider` policy against the test well behind every opt-in. | P1, S4 |
| P2 / S6 | CygNet probe, then UIS command adapter if the route is confirmed. | decision 1, gap 17 |
| Phase 1 and 2 safety | Section 4.8 rows, each as its own pull request. | owner decisions 4, 7 |

Go/no-go for the first real write remains the written sign-off in the readiness document's pilot checklist.
