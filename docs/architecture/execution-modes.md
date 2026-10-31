# Execution modes: Shadow, Recommendation, Autonomous

A Configured Logic revision carries one immutable operating mode. The mode is persisted on the revision, copied onto every
Execution (manual or scheduled), and sent to the worker in the V2 request. The mode only decides what the platform does
with a completed Execution's output; the worker never sends commands itself.

| | Shadow | Recommendation | Autonomous |
| --- | --- | --- | --- |
| Create / activate revision | yes | yes | yes |
| Scheduled execution | yes | yes | yes |
| Manual run (`/shadow` request) | yes | yes | no (scheduler only) |
| Gateway read plan for inputs | yes | yes | yes |
| ControlIntent output | recorded in the result only | materialized as a `Recommendation` | materialized as a `Recommendation` |
| Operator decision | n/a | approve / reject by `recommendations.decide` | reject (veto) only; approval is refused |
| ControlAction / command | never | never | only through the autonomy gate |
| Sink | none | none | simulator only |

## Governance rules

- A **ControlIntent** is what a Logic Module proposes. It has no authority. A **ControlAction** is the governed record of a
  command and is created only by `AutonomousControlService` (never by the worker, the materializer or the decision API).
- Recommendation mode never issues a command. An approval records the operator decision and audit only; a later
  operator-driven dispatch path is deliberately not implemented.
- Autonomous intents are auto-decided by the gate as `system:autonomy-gate`. Humans cannot approve them but can reject
  (veto) an undecided one before the gate claims it.
- The gate (`Application/Control/AutonomousControlService.cs`) refuses unless **all** hold, in order: kill switch released,
  `Control:Autonomous:Enabled=true` (default false), an enabled per-configuration `AutonomyPolicy` exists, the sink is the
  Simulator, the source revision is the active Autonomous revision, the intent has not expired, the command is on the policy
  allowlist, a value is present, the unit matches, and the value is within the inclusive bounds. A claim then enforces the
  rate limit (per configuration, under an advisory lock) and atomically moves the recommendation Created to Approved while
  inserting one `Requested` ControlAction (unique per recommendation, so redelivery cannot double-command). The kill switch is
  re-read after the claim and before dispatch.
- Every refusal is stored as a `Blocked` ControlAction with a gate code, the recommendation is rejected, and an audit event is
  written. Dispatch outcomes are `Dispatched` (recommendation `Executed`) or `Failed` (recommendation `Invalidated`). A
  `Requested` action left by a crash is never auto-redispatched (ambiguous dispatch needs an operator).
- **Real provider writes are disabled.** `SetPolicyAsync` rejects any sink other than `Simulator`
  (`CONTROL_REAL_WRITES_DISABLED`), and the `Provider` sink always refuses (`REAL_PROVIDER_WRITES_DISABLED`).
- Authorization reuses existing permissions: `security.manage` to change policies or the kill switch,
  `recommendations.read` to read them. No new RBAC permission was added.

## Where things run

- Scheduling: `ScheduledShadowSchedulerService` (name kept) dispatches any active revision with an enabled schedule, whatever
  its mode. The hosted service also sweeps for undecided Autonomous recommendations.
- Materialization and gate: `ExecutionResultV2Consumer` materializes after a Completed result, then calls the gate.
- API: `/api/v1/control/kill-switch`, `/control/policies/{configuredLogicId}`, `/control/actions`; configuration create/revision
  requests accept an optional `mode` (default Shadow).
- UI: mode selector in the configuration form; the Recommendations page shows the kill switch and recent control actions.
- Tests: `tests/WellSiteAutoPilot.ExecutionModeSafetyTests` (Shadow never recommends or commands, Recommendation never
  commands, Autonomous only via the gate incl. bounds, rate limit, idempotency, kill switch, global switch, real sink refusal) and
  the mode case in `ScheduledShadowSchedulerTests`.

## Known gaps

- No HTTP command endpoint on the Provider Simulator; the sink is an in-process simulator that records requests.
- No readback verification, ambiguous-dispatch resolution UI, or dedicated control RBAC permissions.
- Autonomy policy editing is API-only (no web editor yet).
- No operator-approved Recommendation to ControlAction path (Recommendation mode stays advisory by design).
- Real provider writes require a separate reviewed design (authority, readback, interlocks) and remain disabled.
