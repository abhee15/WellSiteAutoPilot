# Execution modes

A Configured Logic revision carries one immutable operating mode. The mode is persisted on the revision, copied onto every
Execution it produces (manual or scheduled), and sent to the worker in the request. The mode only decides what the platform
does with a completed Execution's output; the worker never sends commands.

| | Shadow | Recommendation | Autonomous |
| --- | --- | --- | --- |
| Create / activate revision | yes | yes | yes |
| Scheduled execution | yes | yes | yes |
| Manual run | yes | yes | **never** (scheduler only) |
| Control intents | recorded in the result only | materialized as a `Recommendation` for an operator | materialized as a `Recommendation`, decided only by the autonomy gate |
| Command to equipment | never | never | only through the gate, and only to the simulator today |

## One policy, enforced at every layer

`ExecutionModePolicy` (Domain) is the single statement of what each mode may do:

- `IsSupported` — the modes the platform can govern (Shadow, Recommendation, Autonomous);
- `AllowsManualRun` — a person may start it (Shadow, Recommendation; never Autonomous);
- `MaterializesControlIntents` — its intents become `Recommendation` rows (Recommendation, Autonomous).

The policy is applied when:

- a revision is created (`ConfiguredLogicService`, repository draft validation) and when a review/activation is written;
- an Execution request is normalized (`ExecutionService`);
- a person starts a run (`PrepareManualShadowAsync`, the manual-request key match, the grant-checked request path);
- the scheduler picks candidates (the SQL candidate filter and the in-memory check use the same set);
- the live-read plan of an Execution is resolved for the Gateway;
- the worker accepts a request (`EXECUTION_MODE_NOT_SUPPORTED` otherwise);
- a completed Execution's intents are materialized.

SQL queries cannot call the policy, so the candidate filters in `ConfiguredLogicRepository` and `ScopedAccessQueries` spell
the names out. `ExecutionModeSafetyTests` forces a revision with an unrecognized mode into the table and fails if any path
dispatches it or grants manual run authority for it, and `SecurityPersistenceTests` does the same for Autonomous, so drift
between the SQL filters and the policy is caught.

## The autonomy gate

Only `AutonomousControlService` turns Autonomous intents into `ControlAction`s. After an Autonomous Execution's intents are
materialized, and on every scheduler sweep for any intent a crash left undecided, each intent passes, in order:

1. the global kill switch (re-read after the claim, so engaging it mid-pass still stops dispatch);
2. the deployment switch `Control:Autonomous:Enabled` (default **false**);
3. a per-configuration policy: command allowlist, unit match and inclusive numeric bounds;
4. recommendation expiry;
5. a per-configuration rate limit, claimed under an advisory lock.

Every refusal is a `Blocked` ControlAction plus an audit event, and a blocked Autonomous recommendation cannot be approved
by a person afterwards. An accepted intent is claimed atomically (`Created`→`Approved`, one `Requested` ControlAction per
recommendation) and handed to the sink. The only sink that can be configured is the simulator; the provider sink always
refuses with `REAL_PROVIDER_WRITES_DISABLED`. Provider writes are a separate, design-first workstream
(`docs/design/autonomous-real-sink-readiness.md`).

## API

`POST /configured-logic` and `POST /configured-logic/{id}/revisions` accept an optional `mode` (`Shadow` default,
`Recommendation`, `Autonomous`). Responses report the mode.

| Endpoint | Policy | Notes |
| --- | --- | --- |
| `GET /control/kill-switch` | `RecommendationsRead` | global state, includes the deployment switch |
| `PUT /control/kill-switch` | `SecurityManage` | audited |
| `GET /control/policies/{configuredLogicId}` | `RecommendationsRead` | scoped: needs read access to every Asset the configuration touches; a missing and a forbidden configuration look the same |
| `PUT /control/policies/{configuredLogicId}` | `SecurityManage` | simulator sink only |
| `GET /control/actions` | `RecommendationsRead` | scoped in SQL before the limit, by the Asset of the originating recommendation |

## Known limits

- Editing policies needs the global `SecurityManage` policy until a dedicated `control.manage` permission is added.
- A person cannot yet approve an Autonomous recommendation the gate blocked, and operator-approved dispatch, readback and
  ambiguous-dispatch resolution are separate slices.
- Provider commands (ForeSite setpoints, CygNet controller registry values) are not implemented; see workstream L in
  `docs/implementation/poc-master-plan.md` and gaps 20–22 in `docs/implementation/poc-gap-register.md`.
