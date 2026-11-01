# Execution modes

A Configured Logic revision carries one immutable operating mode. The mode is persisted on the revision, copied onto every
Execution it produces (manual or scheduled), and sent to the worker in the request. The mode only decides what the platform
does with a completed Execution's output; the worker never sends commands.

| | Shadow | Recommendation | Autonomous |
| --- | --- | --- | --- |
| Create / activate revision | yes | yes | **not yet** |
| Scheduled execution | yes | yes | not yet |
| Manual run | yes | yes | not yet (scheduler only, once the gate exists) |
| Control intents | recorded in the result only | materialized as a `Recommendation` for an operator | not yet |
| Command to equipment | never | never | not yet |

## One policy, enforced at every layer

`ExecutionModePolicy` (Domain) is the single statement of which modes the platform can govern. A mode is accepted only when
everything it implies is implemented, so **Autonomous is refused today** and no Autonomous revision or Execution can exist.
It is widened together with the control gate that makes Autonomous safe, not before.

The policy is applied when:

- a revision is created (`ConfiguredLogicService`, repository draft validation) and when a review/activation is written;
- an Execution request is normalized (`ExecutionService`);
- a person starts a run (`PrepareManualShadowAsync`, the manual-request key match, the grant-checked request path);
- the scheduler picks candidates (the SQL candidate filter and the in-memory check use the same set);
- the live-read plan of an Execution is resolved for the Gateway;
- the worker accepts a request (`EXECUTION_MODE_NOT_SUPPORTED` otherwise).

SQL queries cannot call the policy, so the candidate filters in `ConfiguredLogicRepository` and `ScopedAccessQueries` spell
out the same two names. `ExecutionModeSafetyTests` forces an `Autonomous` row into the table and fails if any path dispatches
it, so drift between the SQL filters and the policy is caught.

## API

`POST /configured-logic` and `POST /configured-logic/{id}/revisions` accept an optional `mode` (`Shadow` default,
`Recommendation`). Responses already report the mode.

## Not in this change

The autonomy gate, kill switch, control persistence and `control.manage`, operator-approved dispatch, readback, and the web
mode selector are separate slices (see `docs/implementation/poc-master-plan.md` on `feature/poc-integration`).
