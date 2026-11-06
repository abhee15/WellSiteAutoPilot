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

### 4.2 Command limits: mode, state and versions
Superseded in detail by `command-limits.md` sections 3 and 5 (design reviews 1 to 3). In summary: the record is named
**Command limits** and keyed by **(configured logic, mode)**; rows are keyed by (asset role, requirement id, command).

- Shadow: no record exists or can be created.
- Recommendation: states **Off / On**; the limits that bound executing an approved recommendation. No rate, no dry run.
- Autonomous: states **Off / Dry run / Live**; limits plus a rate **per asset and in total**, counting only Autonomous-origin actions.
- Versions: at most one live and one pending. More room creates a pending version; less room may replace the live one.
  Dry run is a pending version with no live one (decided and recorded, never sent, not counted toward the rate).
- The gate requires a Live (or, for Recommendation, On) record whose mode equals the live revision's mode, so switching
  Recommendation to Autonomous never edits a record in place: the Autonomous record is prepared first, against the
  candidate revision.
- Activating a non-Shadow revision requires a record of that mode; activation never raises the state. A revision whose
  module version or bound assets differ drops a Live record to Dry run, with an event.
- Autonomous Live requires recorded dry-run evidence (provider-command-seam decision 6; threshold is an owner decision).
- Edit needs `control.manage` plus `configured-logic.manage` scoped to the logic plus read on every bound asset (today there
  is no scope check on writes at all, F7). Go live needs `control.manage` and a reason, optionally a different person from
  the author. Turn off is immediate for anyone who can edit. Resuming an Autonomous schedule needs `control.manage`.
- Dry run is a limits state, not a sink: `provider-command-seam.md` 4.7 (dry run chosen per policy through `SinkKind`) changes.

Approve and execute stay two steps with two grants (`recommendations.decide`, `control.manage`) but one screen action
refusing a Shadow policy, not as a UI-only change.

## 5a. Decided before P2 (each would be a migration if decided later)

Policy key and state (4.2); approve versus execute (4.2); act-by deadline (F18); the baseline value source (F19);
legacy rows (4.1); audit fields (F17); the catalog's granularity, the platform command vocabulary, the requirement id on module intents, per-asset rate, the live/pending structure and the events table (`command-limits.md` sections 3, 5 and 6). The catalog is keyed by (connection, command) today,
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
