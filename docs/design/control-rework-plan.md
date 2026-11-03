# Control and recommendations: rework plan

Status: proposed, for the owner's decision. Nothing here is built yet. Revised after an independent review that checked
every claim against the code (section 8 lists what it changed).

Why this exists. The control work since PR #367 was built in small steps, backend first and screens as add-ons. Two
reviews against the repository (every merged PR from #367 to #415) and against the original ACL prototype found that some
of it must be reshaped now, while little depends on it, instead of being rebuilt later. This document is the single plan.
It supersedes the ordering in `control-ui-audit.md` section 3 and keeps its Definition of done.

## 1. What the ACL prototype teaches (a reference, not a target)

Keep, because our users need the same things:

- **A decision screen that shows the evidence.** Current and proposed value with the change, the reason in plain words,
  the inputs with their quality and age, the safety checks as pass/warn lines, and a history of who did what.
- **Consequence-first confirmation.** Approving states what will be written, where, and that it will be read back. Rejecting
  states what stays as it is, and asks for a reason category and text.
- **One state vocabulary, a quality word plus an icon (never colour alone), and "last known" for values that are not current.**
- **Mode explained in a sentence** wherever a mode badge appears.

Do not repeat, because the prototype itself got these wrong:

- Values and buttons that go stale: an Approve button stayed live after the controller had changed; the screen found out
  only from a refusal after the click. Our screens ask the server first and show the reason instead of the button.
- A stop with no confirmation, and Run accepted while stopped.
- A list with no sort, filter, search, expiry urgency or keyboard use; the same page twice; no responsive layout.
- Shadow results with nowhere to go. Ours are read in the run and in the Shadow comparison (section 4.6).

## 2. Findings that change the design (from the retrospective)

| # | Finding | Consequence |
| --- | --- | --- |
| F1 | The policy API always selects the in-process Simulator sink, which echoes the commanded value back. A Verified action can mean nothing was sent. The real Gateway sink cannot be reached from the API. | The in-process sink leaves the product (tests only). The sink follows from the target's adapter. |
| F2 | The Gateway command route is hard-wired to the simulator and ignores the connection's settings. | One command provider per adapter, registered by adapter key, using the connection's settings. |
| F3 | A policy has no operating mode. An "execute approved recommendations" policy becomes autonomous authority when an Autonomous revision is activated; activating one needs no policy. | The policy carries its mode. Activation requires a matching enabled policy. |
| F4 | An action did not record where it was sent. | Fixed in #416. |
| F5 | Any fault while materializing a run result (an undeclared command, an asset outside the run's scope, empty required text, an unreadable manifest) happens before the message is acknowledged, so the message is redelivered until the limit. | A bad intent never causes redelivery: drop the intent, record a diagnostic on the run, acknowledge. |
| F6 | Command names and units are compared exactly in the gate and in module declarations, but ignoring case in the catalog lookup and the readback unit check. | One rule everywhere: the adapter's exact spelling and unit, checked at save, materialize and dispatch. Step enforcement needs the value now on the equipment, so it belongs with the preview read (P3), not here. |
| F7 | Policy writes are not scoped like reads and have no version. | Scope writes as reads, require the configuration grant, add an expected version. |
| F8 | Readback runs once, at once, with an exact match, and re-resolves the asset's target as it is today, so a rebound asset would be read at a different device. | Settle-and-deadline verification (framework F7) that reads the stored target; it ships with or before the in-process sink is removed. |
| F17 | Who authorised a write is not recorded: the request is audited as the gate even for a person's execution, the approver's reason is overwritten by a gate code, and the action stores no policy version or prior value. | Executor, policy version, prior value and preview hash are stored on the action (P2b). |
| F18 | A recommendation has no expiry (`ExpiresAtUtc` is never set); the only deadline is a 30-minute age enforced when executing, never when approving, so a person can approve something that can no longer be executed. | One server-computed "act by", enforced at approval and at execution. |
| F19 | The module contract carries no observed value, expiry or explanation, so "the value moved", "reason in plain words" and "inputs with quality" have no source. | The server snapshots the value read at materialization (and the module SDK may add fields); decided before P3. |
| F20 | Open recommendations of a replaced revision stay in the inbox and fail at execute. `Superseded` is never set. | Supersede them when a new revision is activated. |
| F9 | Control screens: no stylesheet, free-text fields, no well named in the open-commands list, "Released" shown in green while the deployment switch is off, Execute offered when the server will refuse. | Rebuilt on shared components; the server returns `canExecute` with a reason. |
| F10 | The UI can only author Shadow revisions and offers Run only for Shadow. | Decide authoring of other modes (section 7, decision 6). |
| F11 | Mode is shown as a raw word in six places and not in the inbox. | One mode badge and a mode filter. |
| F12-F16 | Contradictory schedule text, four error patterns, documents that disagree, a schedule floor that fails open, resuming an Autonomous schedule needs only the configuration grant. | Small fixes and decisions below. |

## 3. Principles for everything below

1. **Applies, or absent.** A control, panel or column that cannot apply to what is on screen is not shown (`control-screens.md` 1a).
2. **The server decides, the screen reports.** Whether a person may execute, what will be written, where, and why not are computed by the server and shown with a reason code. The browser never re-derives a safety rule.
3. **Status says what is known.** One outcome field, derived on the server from status plus gate code, is the only source of the words Sent, Verified, Needs review, Rejected, Stopped, Dry run.
4. **One path for every adapter.** The simulator is a real adapter with a real connection. No in-process shortcut exists in the product.
5. **Rendered and looked at.** Each screen change is rendered before and after, in every state, and the screenshots are reviewed (Definition of done 9 and 10).

## 4. Target design

### 4.1 Sinks and targets
Provider is the only sink in the product. A command's destination is its asset's connection and catalog entry (resolved
by F6a, stored on the action by #416). The Gateway has one command provider per adapter key, using the connection's own
settings; the simulator's is the first. The in-process sink stays as a test double. Dry run is not a sink: it is a state of
the policy (4.2).

Existing rows are not converted. Actions recorded by the in-process sink are marked legacy simulated and cannot be
verified again. Enabled simulator policies that cannot resolve a real target are disabled with a recorded reason.

### 4.2 Policy, mode and state
A policy is keyed by **(configured logic, mode)** and versioned; its state is **Off, Dry run or Live**.

- Shadow: no policy exists or can be created.
- Recommendation: the limits that bound executing an approved recommendation. No rate.
- Autonomous: limits plus the rate.
- The gate requires a Live policy whose mode equals the live revision's mode. Switching a logic from Recommendation to
  Autonomous therefore never edits a policy in place or deadlocks: the Autonomous policy is prepared first, as its own row.
- Activating a Recommendation or Autonomous revision requires a policy of that mode, and the module's declarations are
  checked again against it at activation (a new revision must not inherit a policy approved for a different module version).
- Autonomous Live requires a recorded Dry run first (provider-command-seam decision 6): the dry-run history is the evidence.
- Writing a policy needs the configuration grant and read access to the logic, and carries an expected version.
- Activating a non-Shadow revision, and resuming an Autonomous schedule, need `control.manage` as well as the configuration grant.

Approve and execute stay two steps with two grants (`recommendations.decide`, `control.manage`) but one screen action
"Approve and send" for a person holding both; the audit records both identities. A deployment may require them to be
different people.

### 4.3 Execution preview (server)
For an approved recommendation the server returns `execution`: `canExecute`, a reason code when not, and when it can, a
preview: the target (asset, connection, adapter), the current value, the proposed value, the catalog limits and step, the
settle time and deadline. The screen shows the preview inside the confirmation and nothing it did not receive.

The reason codes include `ADAPTER_CANNOT_COMMAND` (read-only adapters such as CygNet and ForeSite today) and
`GATEWAY_UNAVAILABLE` (the preview needs a live read). For a read-only adapter the person can still approve:
"Approve - I will apply it", and the platform reads the value back afterwards and says whether it changed. Deciding is
never blocked by the Gateway being down; sending is.

### 4.4 Verification
Framework step F7: after a send, wait the entry's settle time, read back, compare within the catalog's tolerance, repeat
until the deadline, then Verified, or Needs review (Ambiguous). The catalog gains a tolerance per command (a change to the
catalog schema and to the catalog screen). A `Sending` marker is written before the send, so a crash between the send and
its record is known to be ambiguous while a crash before it is known to be safe; the sweeper settles Requested and
Dispatched actions on that basis. One command at a time per setpoint: a new action for a setpoint with one in flight is
blocked (`GATE_COMMAND_IN_FLIGHT`), so an Autonomous schedule faster than the deadline cannot stack. Intents in one run are
independent; there are no grouped or atomic commands. Verification uses the stored target and fetches only the settings fresh.

### 4.5 Screens
Specified screen by screen in `control-screens.md` (which is updated before P5 and P6; it currently contradicts this plan).

- **One row status for recommendation and action together**, derived on the server from the recommendation, the action and
  the gate code: *Waiting for you, Approved - not sent, Sent, Verified, Needs review, Declined, Refused by site, Stopped,
  Expired.* "Rejected" never appears alone, so a person's decline and a provider's refusal cannot be confused.
- **Recommendations is one list with one inspector** on a shared table component (built in P4, with arrow-key and Enter
  behaviour; none exists today). Columns: status, asset and logic (both named), command and change, *Act by* (only on rows
  that need action), decided by. The mode is in the inspector header and the filter, not a column: only Recommendation and
  Autonomous appear, and "decided by: the system" already says Autonomous. Filters: status, mode, asset, logic, search.
  Advisory recommendations have their own row shape: the finding in place of a command and change, and no decision bar.
- **Inspector**: facts first (current, proposed, change, bounds), then the reason, inputs with quality and age, safety
  checks as pass/warn lines (the gate evaluated without sending, from the server), then history.
- **Decide** uses the shared in-page confirmation. Approve states what will be written, where, and that it will be read
  back. Decline asks for a reason category; the text is required only for "Other".
- **Stale or changed**: the server compares the value snapshotted at materialization with a fresh read, and the item says so
  and disables the decision with the reason; it does not wait for a refusal.
- **Control status strip**: shown only when something is abnormal (kill switch engaged; a Recommendation or Autonomous
  revision is live while sending is impossible; deployment switch off). Otherwise there is nothing to read. The per-logic
  answer ("can this send") lives in the logic header and in the preview. Stopping asks for confirmation and a reason.
- **Control activity** rows name asset and logic, use the shared table, and open the same inspector style for an action.
- **Mode badge** (shared Status, words through `t()`) wherever logic or a run appears and in the inspector.

### 4.6 Shadow
Shadow produces no recommendations. Its value is the comparison: what it would have done against what happened. Run detail
shows the shadow intent, and the logic Overview can list recent shadow intents next to the live value. This is a separate
design (`shadow-results.md`, to be written) and is not part of the control rework.

## 5. Order of work

Each step is one small pull request listing the modes, adapters and permissions it applies to, with a test per cell.

| Step | Change | Notes |
| --- | --- | --- |
| P0 | **Screen harness**: render named screens and states to PNG from mocked data. | First needs the API client generated locally (the app cannot be built in the working environment today); solving that is part of P0. |
| P1 | F5 (a bad intent never redelivers) and F6 (one name and unit rule). | Small and independent. |
| Spec | Update `control-screens.md` and the framework document to this plan. | Before P5/P6, per Definition of done 1. |
| P2a | Gateway: one command provider per adapter key, settings from the connection, readback by the stored target. | Gateway project only; no schema change. |
| P2b | **The one control migration**: policy (logic, mode, version, state), executor, policy version, prior value, preview hash and `Sending` marker on the action, legacy rows, scoped versioned writes, activation checks, in-process sink removed from the product. | Ships with or after P3 verification, never before it. |
| P3 | Settle, deadline, tolerance, in-flight rule, sweeper; baseline value; "act by"; outcome status; `execution` preview with `canExecute`, step enforcement. | Makes the screens truthful. |
| P4 | Shared Notice, fields, ConfirmPanel and table; mode badge; control screens converted. The guard stops new raw controls. | Other pages move in separate PRs. |
| P5 | Recommendations list and inspector, decision, decline, stale handling, status strip. | |
| P6 | Control activity, policy and catalog screens per mode; schedule panel; schedule text (F12). | |
| P7 | Runbook and poc-master-plan brought up to date. | The specifications were already updated in Spec. |

The Shadow-only hiding of the policy panel (branch `feat/control-panel-by-mode`) lands inside P2b/P6 with the server
refusing a Shadow policy, not as a UI-only change.

## 5a. Decided before P2 (each would be a migration if decided later)

Policy key and state (4.2); approve versus execute (4.2); act-by deadline (F18); the baseline value source (F19);
legacy rows (4.1); audit fields (F17); and the catalog's granularity. The catalog is keyed by (connection, command) today,
so an ESP and a rod pump on one ForeSite connection cannot have different addressing or limits for the same command, and a
command declared for a role can be sent to any asset in scope. Decision 13 below settles this.

## 6. What does not change
Gate order, kill switch semantics, idempotency key, closed Gateway answers, no retry of a send, the closed allow-list of adapters, the catalog's versioning and audit, grants.

## 7. Decisions for the owner (with a recommendation)

1. **Sink model.** The in-process sink leaves the product; dry run is a policy state. Recommended: yes.
2. **Policy.** Keyed by (logic, mode), versioned, state Off / Dry run / Live; activation requires a matching policy. Recommended: yes.
3. **Executing approved recommendations.** Its own Recommendation-mode policy, no rate. Recommended: yes.
4. **`control.manage`.** Stays deployment-wide, and is also required to activate a non-Shadow revision. Recommended: yes.
5. **Resuming an Autonomous schedule.** Requires `control.manage` as well. Recommended: yes.
6. **Authoring other modes in the UI.** Recommendation first, including the read-only "apply by hand" path; Autonomous after verification is proven. Recommended: yes.
7. **Control history location.** Operations, and a logic's own actions on its Activity tab. Recommended: yes.
8. **Status words.** One derived row status for recommendation and action together (4.5). Recommended: yes.
9. **A bad intent from a module.** Drop and record a diagnostic; never redeliver. Recommended: yes.
10. **Unknown adapter schedule floor.** Fail closed for Recommendation and Autonomous revisions, open for Shadow. Recommended: yes.
11. **Advice in Autonomous mode.** Record it on the run. Recommended: yes.
12. **Control status strip.** Only when abnormal. Recommended: yes.
13. **Catalog granularity.** Per (connection, asset type, command), with the module's declared role checked against the asset's role. Recommended: yes, now.
14. **Approve and send.** One screen action for a person holding both grants, two audited identities, optional two-person separation per deployment. Recommended: yes.
15. **Single writer.** One in-flight command per setpoint. Recommended: yes.
16. **Kill switch scope.** Global now; per connection later without changing the screens. A narrower grant to stop than to change a policy is worth discussing. Recommended: global, `control.manage`.
17. **Dry run before Live.** Autonomous Live requires a recorded dry run. Recommended: yes.

## 8. What the independent review changed

It confirmed F1-F4, F7, F8 and the target design, and corrected the plan: F5 is wider than undeclared commands; step
enforcement needs a read the gate does not make; the catalog has no tolerance; recommendations have no expiry; no table
component with keyboard behaviour exists; the status list needed one vocabulary; the policy cannot be keyed by logic alone;
the in-process-sink removal must not precede verification; P2 was split; P4 was narrowed; the specification moves ahead of
the screens; read-only adapters, gateway outage, audit fields, in-flight commands and legacy rows are now first-class.
