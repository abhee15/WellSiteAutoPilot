# Control and recommendations: rework plan

Status: proposed, for the owner's decision. Nothing here is built yet.

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
| F5 | One undeclared command in a module result blocks the whole result and restarts delivery. | Drop the command, record a diagnostic on the run, acknowledge the message. |
| F6 | Units and command names are matched by different rules in different layers; `maxStep` is never enforced. | One rule everywhere: the adapter's exact spelling and unit, checked at save, materialize and dispatch; enforce step. |
| F7 | Policy writes are not scoped like reads and have no version. | Scope writes as reads, require the configuration grant, add an expected version. |
| F8 | Readback runs once, at once, with an exact match. | Settle-and-deadline verification (framework F7) before the Provider sink can be chosen. |
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
Provider is the only sink. A command's destination is its asset's connection and catalog entry (already resolved by F6a).
The sink kind stops being a policy setting. The Gateway has one command provider per adapter key; the simulator's is the
first. The policy field `SinkKind` and the in-process sink are removed after a migration that maps existing rows.

### 4.2 Policy and mode
A policy is written for one mode and states it. Shadow: no policy exists or can be created. Recommendation: the policy is
the limits that bound execution of an approved recommendation. Autonomous: limits plus the rate. The gate requires the policy's mode to equal the
live revision's mode. Activating a Recommendation or Autonomous revision requires an enabled policy of that mode (a draft
may exist without one); a missing policy is shown on the revision, not discovered as a Blocked action later.

### 4.3 Execution preview (server)
For an approved recommendation the server returns `execution`: `canExecute`, a reason code when not, and when it can, a
preview: the target (asset, connection, adapter), the current value, the proposed value, the catalog limits and step, the
settle time and deadline. The screen shows the preview inside the confirmation and nothing it did not receive.

### 4.4 Verification
Framework step F7: after a send, wait the entry's settle time, read back, compare within the catalog's tolerance, repeat
until the deadline, then Verified, or Needs review (Ambiguous). A sweeper settles actions stuck in Requested or Dispatched.

### 4.5 Screens
Specified screen by screen in `control-screens.md`; the changes this plan adds:

- **Recommendations is one list with one inspector**, on the shared table (status first, row opens details). Columns: status,
  asset and logic (both named), command and change, mode badge, expires (a countdown, with tone as it nears), decided by.
  Filter toolbar: status, mode, asset, logic, search. Sort by expiry or age. Keyboard: arrows and Enter, as other tables.
- **Inspector**: facts first (current, proposed, change, bounds), then reason, inputs with quality and age, safety checks
  as pass/warn lines from the server, then history. The decision bar is the server's preview and reason, never a guess.
- **Decide** uses the shared confirmation (in the page, never a browser dialog). Approve states what will be written and
  read back. Reject asks for a reason category and text and states what stays as it is.
- **Stale or changed**: opening an item asks the server for the preview. If the value moved, the item says so and the
  decision is disabled with the reason; it does not wait for a refusal.
- **Control status strip** on every page (it cannot be hidden): deployment switch, kill switch, and whether any command can
  be sent right now, in one truthful line. Stopping asks for confirmation and a reason.
- **Control activity** rows name asset and logic, use the shared table, and open the same inspector style for an action.
- **Mode badge** (shared Status, words through `t()`) wherever logic, a run or a recommendation appears.

### 4.6 Shadow
Shadow produces no recommendations. Its value is the comparison: what it would have done against what happened. Run detail
shows the shadow intent, and the logic Overview can list recent shadow intents next to the live value. This is a separate
design (`shadow-results.md`, to be written) and is not part of the control rework.

## 5. Order of work

Each step is one small pull request with the modes, adapters and permissions it applies to listed and tested.

| Step | Change | Why here |
| --- | --- | --- |
| P0 | **Screen harness**: render named screens and states to PNG from mocked data, with a command to run it. | Needed to look before and after, from now on. |
| P1 | F5 (drop and diagnose), F6 (one name and unit rule, enforce step), F7 (scoped, versioned policy writes). | Small, independent, no schema risk for the rest. |
| P2 | F2 (command provider per adapter), F1 (Provider only; map existing rows; remove the in-process sink from the product), F3 (policy mode). | The core reshape; one migration. |
| P3 | Framework F7 (settle, deadline, tolerance, sweeper); outcome field; `execution` preview and `canExecute`. | Makes the screens truthful. |
| P4 | UI D1: shared Notice, fields, ConfirmPanel across the whole app (not only control). Mode badge. | Foundation for every screen. |
| P5 | Control status strip; Recommendations list and inspector; decision and reject; stale handling. | The main screen. |
| P6 | Control activity, policy and catalog screens per mode; schedule panel; schedule text (F12). | Depends on P2 and P3. |
| P7 | Documents reconciled (F14); runbook; poc-master-plan. | Last, so they describe what exists. |

The small Shadow-only hiding of the policy panel is done as part of P2/P6, with the server refusing a Shadow policy, not
as a UI-only change.

## 6. What does not change
Gate order, kill switch semantics, idempotency key, closed Gateway answers, no retry of a send, the closed allow-list of adapters, the catalog's versioning and audit, grants.

## 7. Decisions for the owner (with a recommendation)

1. **Sink model.** Remove the in-process simulator sink from the product, so Verified always means a connection was read. Recommended: yes.
2. **Policy and mode.** The policy states its mode; activating a non-Shadow revision requires an enabled policy of that mode. Recommended: yes.
3. **Executing approved recommendations.** Same limit shape as Autonomous but its own policy (Recommendation mode), no rate. Recommended: yes, so one mode never grants the other.
4. **`control.manage` scope.** Keep deployment-wide for control writes, but require the configuration grant for policy writes. Recommended: yes for now; revisit with scoped grants.
5. **Resuming an Autonomous schedule.** Require `control.manage` as well as the configuration grant, because resuming restarts sending. Recommended: yes.
6. **Authoring other modes in the UI.** Add Recommendation authoring first (manual run allowed); Autonomous after verification is proven. Until then control screens are reachable only for logic made through the API, and say so in the empty state. Recommended: yes.
7. **Control history location.** Operations (as `control-screens.md`), with the actions of one logic also on its Activity tab. Recommended: yes.
8. **Status words.** The six outcome words, derived on the server. Recommended: yes.
9. **Undeclared command from a module.** Drop the command and record a diagnostic on the run. Recommended: yes.
10. **Unknown adapter schedule floor.** Fail closed for adapters that can be commanded, open for read-only. Recommended: yes.
11. **Advice in Autonomous mode.** Record it on the run instead of discarding it. Recommended: yes.
12. **Control status strip.** On every page. Recommended: yes.
