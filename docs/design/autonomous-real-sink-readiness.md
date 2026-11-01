# Autonomous real-sink readiness (CygNet / ForeSite)

Status: design + read-only discovery. **The shared design for the command seam is now [provider-command-seam.md](provider-command-seam.md);
it supersedes the outcome model in section 3, the ambiguity item in section 2 (built) and the dry-run description in section 4
(not on `develop`). This document stays as the source survey and safety checklist.**

Original status: design + read-only discovery. **Nothing here was executed against any CygNet or ForeSite system.** Findings come
from reading source under `ForeSiteService\Trunk` (paths relative to it). Real provider writes stay disabled; the only
code change is a sink that never writes (section 4).

## 1. Capability survey

### ForeSite (write path exists, and is the better integration point)

| Capability | Where | Notes |
| --- | --- | --- |
| Send setpoints for a well | `Server\POPServer.CoreWcfHost\Services\SurveillanceService.Setpoints.Net10.cs` `SendSetPointsParamsForSingleWell` (l.1533/1546); also `SendMultipleSetPointParamsForSingleWell`, `SendAndGet...`, `SendSavedSetPointsParamsForSingleWell` | CoreWCF operations. Resolves the well to a CygNet facility, looks up the data-group config, builds a UIS command and sends it. |
| Autonomous idle-time optimisation (existing ForeSite autonomous control) | `...Services\SurveillanceService.AutonomousControl.Net10.cs` `OptimizingRRLIdleTimeForSingleWell` | Reads setpoint, bounds new value with system settings (`MINIMUM/MAXIMUM_IDLE_TIME_IN_SECONDS`, incremental step), sends it, records history and alarms. Prior art for bounds + step limits. |
| UIS command execution | `Server\Support\CygNetWrapper\CygNetWellManager.cs` `ExecuteNamedUisCommand` (l.894) -> `UisClient.SendUISCommand(facilityId, commandName, params, null)` (l.876) | Returns `bool?`: `true` sent, `false` failed, `null` command not found. "Sent" is not "applied". |
| Write confirmation | `CygNetWellManager.GetTokenizedDataGroupTransaction` (l.1238) | Polls DDS for a data-group transaction whose UIS parameters contain a unique token, newer than send time minus 15 min (clock-skew allowance), up to `CYGNET_COMMAND_TIMEOUT` (default 2 min). Confirms the device round trip, not the final point value. |
| Direct DG transaction send | `CygNetWellManager.cs` l.1951 `UisClient.SendDataGroupTxData` | Lower-level; not used for setpoints. Do not use. |
| CygNet libraries | `Libraries\CygNet\CxUis64.dll`, `CxPnt64.dll`, `CygNet.API.Points*.dll`, `CygNet.API.Devices*.dll`, `CygNet.API.Cache.*` | Binary only in this tree. A direct point-write API (CxPnt / `CygNet.API.Points`) may exist but **was not inspected or verified**; ForeSite itself writes only through UIS commands. |

Auth, licensing, idempotency, failure semantics:

- **Caller authorisation:** `ThrowIfUserLacksPermission(PermissionId.IssueWellCommands)` (read: `ScanDevice`). The caller must map to a
  `WindowsIdentity`; otherwise the send is refused ("POP user cannot be mapped to a WindowsIdentity"). CygNet enforces its own
  access level and ForeSite maps an "Access level" exception to a permission failure. So a service principal needs both a
  ForeSite permission and CygNet UIS command rights for that identity.
- **Licensing:** setpoint operations call `ThrowIfFeatureNotLicensed(GetSystemFeatureForWellType(...))`; the autonomous path
  additionally requires `SystemFeature.AutonomousControl` when `FORESITE_FOR_EDGE` is on. A WSA sink must surface licence refusal as a
  distinct, non-retryable failure.
- **Idempotency:** none in the UIS call. The only dedupe handle is the token UIS parameter used for transaction matching
  (`TOKEN_UIS_PARAM_NAME`, `ConstructUISCommand`, l.1855), set only when `waitForResult=true`. A retry of an ambiguous send re-writes.
  WSA's per-recommendation unique ControlAction is the only idempotency barrier and must stay authoritative.
- **Failure semantics:** the setpoint call returns a **status string** (`"success"` or free text), not an exception or code; several
  failure branches only log. Exceptions inside the send are swallowed into a status message and a CygNet-service-shutdown code
  is detected by message text. Partial multi-setpoint sends are per-item. Treat anything other than exact `"success"` as
  *unknown*, and verify by readback.
- **Readback:** `GetSetPointsParamsForSingleWell` issues a UIS "get" command and waits on a DDS transaction (same timeout), i.e. a
  device scan. Slow and itself a device command (needs `ScanDevice`). This is a *command* to the RTU, so for dry-run it is
  **not** an acceptable readback. See below.
- **Audit:** ForeSite writes an audit row per changed setpoint only when the transaction confirmed (`SaveSetPointAuditLogs`,
  l.2222, category `SetPoints`, old/new value, unit).
- **Writability is configuration-driven:** `IsWritable` is true only when the data group has a `UISSendCommandName`.

### CygNet directly

- UIS commands via `IUisClient.SendUISCommand` (above), data-group transactions via DDS, and point values via the Points/CVS APIs
  (`CxCvs64`, `CygNet.API.Points`). Existing WSA CygNet integration is read-only (`src\integration\WellSiteAutoPilot.Integration.CygNet`
  reads current point values). No write surface exists in WSA, and writing directly to CygNet bypasses ForeSite's licence,
  permission, bounds and audit.
- Not verified from source: CygNet's own command authorisation model, point-write handshake/acknowledgement semantics, and
  whether `CygNet.API.Points` exposes a safe control-write. These need vendor documentation or a CygNet test domain.

**Conclusion:** the recommended real sink is a **ForeSite-mediated sink** (call `SendSetPointsParamsForSingleWell` with
`waitForResult=true` through ForeSite's API), not a raw CygNet writer. It inherits permission, licence, audit, and the DDS
confirmation. Raw CygNet writes should not be built in the pilot.

## 2. Safety checklist beyond today's gate

Existing: kill switch, global flag, per-config policy, command allowlist, inclusive bounds, rate limit, expiry, single claim per
recommendation, mode/revision checks.

Required before any real write:

1. **Interlocks:** refuse while well is in alarm / shut-in / local-override, device comms stale, or data quality bad; require
   fresh input values (age limit); refuse if another command to the same target is in flight.
2. **Change windows:** per-policy allowed time-of-day/day windows and site blackout calendar (UTC + site tz).
3. **Operator confirmation tier:** first N writes per policy require human approval (the deliberately-absent Recommendation to
   ControlAction path); autonomous only after a probation period. Policy field `RequiresConfirmation`.
4. **Rate of change:** max step per write and max cumulative drift per day relative to a recorded baseline (mirrors ForeSite
   incremental idle-time limits), not only absolute bounds.
5. **Readback and rollback:** read the current value *before* (captured as rollback value and stored on the action), write,
   confirm after (DDS token transaction **and** value readback within tolerance and timeout). On mismatch/timeout: mark
   `Unverified`, engage per-policy auto-halt, alert; optional automatic revert to the stored prior value (itself gated, one attempt).
6. **Site/well allowlist:** explicit asset/facility allowlist on the policy in addition to the command allowlist, plus a deployment
   allowlist of ForeSite endpoints; start with a single test well.
7. **Dual control:** enabling a real-sink policy or widening bounds needs two distinct `security.manage` users, with a new
   dedicated permission (`control.policy.manage`, `control.killswitch`) rather than reusing `security.manage`.
8. **Audit:** every attempt (including dry-run and blocked) stores actor/service identity, policy snapshot hash, prior value,
   requested value, ForeSite status string, token, readback result and correlation to the execution/recommendation. Reconcile
   against ForeSite's own audit log (`AuditLogService`, category `SetPoints`).
9. **Ambiguity handling:** a `Requested` action with unknown outcome stays operator-resolved (as today); add an
   operator "resolve" API and a circuit breaker (N unverified/failed results opens the policy).
10. **Identity:** a dedicated least-privilege service account mapped to a ForeSite user with only `IssueWellCommands` for allowlisted
    wells, and CygNet access level for only the needed UIS commands.
11. **Separation:** real sink disabled by default in every build; enabling requires config + policy + licence + allowlist; a
    startup check refuses to start with a real sink if the kill switch store is unreachable (fail closed).

## 3. Mapping onto `IControlCommandSink`

Today (`Application\Control\ControlPorts.cs`): `DispatchAsync(ControlDispatchRequest) -> ControlDispatchResult(Accepted, Reference, Detail)`;
the gate calls it once after claim and maps `Accepted` to Dispatched/Failed (see `docs\architecture\execution-modes.md`).

Gaps for a ForeSite sink:

| Need | Change |
| --- | --- |
| Target resolution (asset to ForeSite well id, data group, setpoint element) | Request needs a resolved `ControlTarget` (provider connection id, external id, element id). Resolve server-side from the authorised connection, never from module output. Today only `AssetId` + `Command` string. |
| Tri-state outcome | Replace `Accepted: bool` with `Outcome { Applied, Rejected, Unverified, DryRun }` + machine code. `Unverified` maps to a new `ControlActionStatus.Unverified` (operator action, halts policy). |
| Prior value / rollback | Result carries `PriorValue`, `ObservedValue`; persist both on `ControlAction` (new columns). |
| Readback | Add `IControlCurrentValueReader` (added now, read-only) and a post-write `VerifyAsync` step in the sink or gate with tolerance/timeout from policy. |
| Pre-dispatch checks | Gate gains interlock/window/allowlist checks evaluated before claim, and a re-check after claim like the kill switch. |
| Dispatch hygiene | Do not hold the DB claim open during a 2-minute DDS wait; keep `Requested` state, run the sink with its own timeout, and complete in a separate call. |
| Licence/permission errors | Distinct non-retryable codes (`SINK_LICENSE`, `SINK_FORBIDDEN`). |
| Registration | Sink kind `ForeSite` only registered when explicitly configured; `SetPolicyAsync` currently allows only Simulator/DryRun and must stay so until the checklist is signed off. |

## 4. Dry-run sink (implemented, no writes)

- `ControlSinkKinds.DryRun`; `DryRunControlCommandSink` (`Application\Control\DryRunControlSink.cs`). Its only collaborator is
  `IControlCurrentValueReader` (read-only). It has no write dependency, records the would-be write (`Records`), is idempotent per
  `IdempotencyKey`, and returns `Accepted=true` with detail `DRY RUN: would set <cmd> to <v> <unit>; current <x>. No write performed.`
  and reference `dryrun:<actionId>`.
- The full gate still applies (kill switch, flags, policy, allowlist, bounds, expiry, rate limit, claim). The action's `Sink` is
  `DryRun`, so it is distinguishable in `/control/actions`; the recommendation shows Executed (known limitation until the
  `DryRun` outcome/status exists, see section 3).
- **Config:** `Control:Autonomous:DryRunSink=true` makes `PUT /control/policies/{id}` create DryRun policies instead of Simulator
  ones (default false). `Control:Autonomous:Enabled` must still be true. `Provider` remains refused.
- **Readback:** the default registered reader is `UnavailableControlCurrentValueReader` (returns null, "current unavailable").
  A real reader should use the existing read-only CygNet/ForeSite measurement gateway (`IExecutionMeasurementGateway.GetCurrentAsync`,
  point value read, no device scan). It must **not** use ForeSite `GetSetPointsParams*` because that sends a UIS get command to the device.
- Tests: `tests\WellSiteAutoPilot.ExecutionModeSafetyTests` (DryRun records only in-bounds intent with readback, keeps bounds,
  never reaches the simulator).

## 5. Pilot readiness checklist

- [ ] Owner decision: ForeSite-mediated sink (recommended) vs raw CygNet; vendor confirmation of CygNet write semantics.
- [ ] Dedicated test domain/well (no production), with ForeSite `AutonomousControl` licence and a mapped service identity.
- [ ] Dry-run soak: >= 2 weeks of DryRun on the target well; compare would-write vs operator actions and ForeSite's own autonomous logic.
- [ ] Read-only current-value reader wired and measured (latency, quality, staleness).
- [ ] Section 2 items 1-9 implemented and tested; outcome model (`Unverified`) and rollback in place.
- [ ] New RBAC permissions + dual control; policy editor UI; operator resolve flow for ambiguous actions.
- [ ] Runbook: kill switch drill, rollback drill, on-call, alert routing, comms-loss behaviour.
- [ ] Confirmation tier: first N writes human-approved.
- [ ] Security review of the service identity, secrets, and network path to ForeSite.
- [ ] Written go/no-go sign-off from owner and site operations.

## 6. Open questions for the owner

1. Is ForeSite the sanctioned control path, or is direct CygNet access acceptable to the business/vendor?
2. Which setpoints/wells/device types are in scope for the pilot, and what are engineering bounds and max step per command?
3. Who is the identity of record for a machine-issued command (ForeSite user mapping, audit expectations, regulatory needs)?
4. Does WSA coexist with ForeSite's own `OptimizingRRLIdleTimeForSingleWell`? Need a single-writer rule per setpoint.
5. Required verification standard: DDS transaction only, or value readback with tolerance? Timeout and failure policy (auto-revert vs halt)?
6. Is the existing UIS call acceptable to run from a service host (impersonation via `WindowsIdentity`), and where does it run (ForeSite server vs WSA host)?
7. Licensing: is `AutonomousControl` licensed for the pilot site, and does WSA-initiated control need its own feature flag?
8. Change-window, interlock, and blackout rules per site; who maintains them?
9. Should DryRun results show as a distinct status in the UI rather than Executed?
