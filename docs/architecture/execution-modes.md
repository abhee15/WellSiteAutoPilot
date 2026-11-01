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

## Executing an approved recommendation

A Recommendation-mode run only advises. After a person approves one of its recommendations, a person with `control.manage`
may execute it through the same gate (`POST /control/recommendations/{id}/execute`). Approval is the authority, but nothing
else is waived:

- the configuration must have opted in (`AllowApprovedRecommendations`, off by default), on top of an enabled policy;
- the kill switch, deployment switch, command allowlist, unit, bounds, expiry and rate limit apply as for Autonomous;
- the approval must be a person's (the gate's own decisions never qualify) and must be fresh: older than
  `Control:Autonomous:ApprovedRecommendationMaxAgeMinutes` (default 30, 1 to 1440) it is refused, because it describes a
  plant that has since moved on;
- the revision must still be the active one and still in Recommendation mode;
- the caller must be able to decide that recommendation on its Assets (`control.manage` alone is deployment-wide), and a
  recommendation they cannot decide is reported as not found.

A refusal is audited and leaves the recommendation Approved so the cause can be fixed and the execution retried. A
recommendation can only ever produce one `ControlAction` (unique per recommendation, serialized by the same advisory lock as
the rate limit), so a repeated or concurrent execute returns the existing action instead of sending twice. The action
records its `Origin` (`Autonomous` or `ApprovedRecommendation`).

## API

`POST /configured-logic` and `POST /configured-logic/{id}/revisions` accept an optional `mode` (`Shadow` default,
`Recommendation`, `Autonomous`). Responses report the mode.

| Endpoint | Policy | Notes |
| --- | --- | --- |
| `GET /control/kill-switch` | `RecommendationsRead` | global state, includes the deployment switch |
| `PUT /control/kill-switch` | `ControlManage` | audited |
| `GET /control/policies/{configuredLogicId}` | `RecommendationsRead` | scoped: needs read access to every Asset the configuration touches; a missing and a forbidden configuration look the same |
| `PUT /control/policies/{configuredLogicId}` | `ControlManage` | simulator sink only; sets the approved-execution opt-in |
| `POST /control/recommendations/{recommendationId}/execute` | `ControlManage` | plus decide scope on the recommendation's Assets; see above |
| `GET /control/actions` | `RecommendationsRead` | scoped in SQL before the limit, by the Asset of the originating recommendation |

## Known limits

- `control.manage` is deployment-wide and held by the protected Admin role; other roles opt in through the role catalog. It is not
  yet scoped to Assets or configurations.
- A person cannot approve an Autonomous recommendation the gate blocked.
- Readback and ambiguous-dispatch resolution are a separate slice: today a sink exception is recorded as `Failed`, which is
  only safe because the simulator is the sole sink. It must become "ambiguous" before any real sink exists.
- Provider commands (ForeSite setpoints, CygNet controller registry values) are not implemented; see workstream L in
  `docs/implementation/poc-master-plan.md` and gaps 20–22 in `docs/implementation/poc-gap-register.md`.
