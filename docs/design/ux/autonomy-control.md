# 2. Autonomy policy editor and kill switch

## Purpose
Let authorised people define what an autonomous configuration may do (which commands, within which bounds, at what rate), see every gate decision, and stop everything instantly. Plan: E3 (UI), E4 (resolution controls), E7/E9 later.

Today: `AutonomousControlPanel` (inside Recommendations) shows the kill switch state, an engage/release button (gated by `security.manage`) and the last 20 control actions. This design replaces the gate with `control.manage`, adds the policy editor and scopes everything per configuration.

## Who
- View policies, actions and kill switch: `recommendations.read` (kill switch state is visible to anyone who can see recommendations, so nobody is surprised by blocked commands).
- Edit policy, engage/release kill switch, resolve ambiguous dispatches: `control.manage`.
- Scope: per configuration (a policy belongs to one configured-logic configuration). Kill switch has two scopes: **this configuration** and **all autonomous control** (global, `control.manage` with global scope).

## Entry points
1. **Recommendations > Autonomous control** (current location; keep).
2. **Logic > Configurations > (configuration) > Autonomy tab** (policy editor in context).
3. A persistent banner across the app when the kill switch is engaged (all roles with `recommendations.read`).
4. Run detail > gate decision links to the policy rule that decided.

## Layout
```
┌ Autonomous control ──────────────────────────────────────────────────────────┐
│ ┌ Sink: SIMULATOR ───────────────────────────────────────────────────────┐ │
│ │ ◻ Commands go to the simulator. No real equipment is written.          │ │
│ └────────────────────────────────────────────────────────────────────────┘ │
│ Kill switch   ● Released   Server autonomous control: On                    │
│   [Engage kill switch…]   scope: ( ) This configuration (•) All control      │
│                                                                              │
│ [Configuration ▾ Fillage – W12]    Policy: ● Enabled   [Disable policy]      │
│ ┌ Policy ──────────────────────────────┐ ┌ Gate decisions (this config) ──┐ │
│ │ Permitted commands (allowlist)       │ │ Status Command   Gate   When   │ │
│ │  [x] SetPumpSpeed   unit Hz          │ │ ● Sent  SetSpeed Allowed 12:01 │ │
│ │       Minimum [ 20 ]  Maximum [ 55 ] │ │ ✕ Blocked SetSpeed Out of bounds│ │
│ │       Max change per command [ 5 ]   │ │ ? Unknown SetSpeed Needs check │ │
│ │  [ ] SetChokeSize  (not allowed)     │ └────────────────────────────────┘ │
│ │ Rate limit  [ 3 ] commands per [1 hour ▾]                               │ │
│ │ Requires approval  (•) Always  ( ) Only above bounds-soft limit         │ │
│ │ Sink  Simulator (only option in this build)                             │ │
│ │ [Save policy]  [Discard]    Revision 4 · saved by Ana 11:58             │ │
│ └──────────────────────────────────────────┘                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Simulator versus real sink
- A **sink badge** is always visible in the panel header and on every action row: "Simulator" (info tone, neutral) or "Real equipment" (warning tone with a distinct shape, never colour alone). In the PoC only Simulator exists and the badge is fixed; a Real sink option is not rendered at all (do not show disabled future writes) until E7 completes.
- Copy for the badge: "Simulator: commands are not sent to real equipment." Real (future): "Real equipment: commands change field equipment."
- Any screen that could cause a real write (future) requires typing the configuration name to confirm.

### Policy fields
Allowlist of commands (checkbox per command defined by the module's proposed actions), per-command unit and bounds (min, max, max step), rate limit (count per period, per configuration), approval requirement, enabled. All fields validate on blur: min ≤ max, positive numbers, units from the command definition (not free text).

### Kill switch
- Engage: confirmation dialog stating the effect ("All autonomous commands for [scope] are blocked immediately. Queued commands are cancelled.") with a required optional reason field; the primary button reads "Engage kill switch". Engaging is intentionally low friction (one confirm).
- Release: confirmation with reason; shows the number of blocked/cancelled actions.
- While engaged: red-toned banner app-wide, with `role="alert"` on change.

### Ambiguous dispatch resolution (E4)
Actions in unknown state show a row action "Resolve…" that opens a panel: readback value vs expected, choices "Confirm it was applied", "Confirm it was not applied", with mandatory note. Needs `control.manage`.

## States
| State | Behaviour |
|---|---|
| Loading | `LoadingIndicator` per region; kill switch region shows "Checking kill switch…" and the button stays disabled (never default to "Released"). |
| Error | `role="alert"` "Control status could not be loaded." [Retry]. If the kill switch state is unknown, show "Kill switch state unknown" with warning tone. |
| Empty policy | "No policy for this configuration. Autonomous commands are blocked until you create one." [Create policy]. |
| Empty actions | "No control actions yet." |
| Stale | Kill switch and action list older than 2 min while disconnected: warning "Not live. Last updated 3 min ago." |
| Server disabled | "Autonomous control is disabled on this server." Policy editable, but a note says it has no effect. |
| Read-only | Without `control.manage`: fields shown as text, buttons hidden, note "Only people with control access can change this." |
| Conflict | Revision mismatch on save: show diff of policy fields; [Reload] [Overwrite]. |
| Save failure | Inline and summary; the policy is not applied partly. |

## Real-time
`control` area (exists, G1) refetches kill switch, policy and actions. `recommendations` area updates the awaiting-approval counts that appear next to policy. Kill switch changes must reach all open sessions: the banner subscribes to `control`. Polling fallback as in README.

## Copy
Headings: "Autonomous control", "Policy", "Permitted commands", "Gate decisions". Fields: "Minimum", "Maximum", "Largest change per command", "Commands per hour". Gate reasons in plain words: "Blocked: above the maximum of 55 Hz", "Blocked: rate limit reached (3 per hour)", "Blocked: kill switch engaged", "Blocked: policy disabled", "Blocked: command not permitted". Raw `gateCode` in secondary text.

## Accessibility
- Banner and kill switch status are `role="status"`; engage/release outcomes `role="alert"`.
- Kill switch button is reachable early in tab order and has a distinct accessible name including scope.
- Allowlist is a group of checkboxes; bounds inputs use `inputmode="decimal"`, units announced in label ("Maximum, hertz").
- Confirmation dialog traps focus, Escape cancels, focus returns to the button.
- Sink badge text, not colour, carries "Simulator".

## Open questions
1. Is `control.manage` scoped via the existing grant model (per configuration/asset) and can a global kill switch be reserved for a smaller role?
2. Should the kill switch be usable by operators with only `recommendations.decide`? (Safety argues yes: stopping should be easier than starting.)
3. Per-command bounds in the policy versus bounds declared by the module: which wins, or must the policy be tighter?
4. Is approval always required for autonomous commands in the PoC, or can the policy auto-execute inside soft bounds?
5. Policy versioning: immutable revisions with audit (assumed) and who approves a loosened bound (four-eyes)?
6. What does a future real sink require in the UI (E7 output: interlocks, change windows, operator confirmation, rollback)?
