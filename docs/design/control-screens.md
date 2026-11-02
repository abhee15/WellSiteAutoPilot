# Control screens: layout and UX specification

Status: **proposed, for review.** Written before the screens are built (F8 of `control-command-framework.md`) so that
the layout is decided once, kept simple, and checked against the requirements. Each screen is updated in the same pull
request that changes it. The provider simulator is the first adapter these screens run against; nothing here is specific
to it, and nothing here is built until it can be exercised end to end in CI.

## 1. Principles (the requirements, in plain terms)

1. **Say what is true, no more.** "Sent" is never shown as "applied". Every status says what the platform knows.
2. **One obvious next step per screen.** One primary button per area (`ui-conventions.md`). Consequential actions use text.
3. **Show the ceiling beside the choice.** A person choosing a limit sees the range the site allows, before they type.
4. **No free text where a list exists.** Commands, units and targets are picked, never typed, so a typo cannot reach equipment.
5. **Quiet when nothing is wrong.** Healthy state is a single line. Problems are one banner with one action.
6. **Same components everywhere.** `ActionButton`, `Status`, the shared table, `FilterToolbar`. No per-page styling.
7. **Calm, plain words.** Short sentences, no jargon, no exclamation marks. Copy lives in the localization file.

## 2. Screen inventory

| # | Screen | Lives in | Who | Purpose |
| --- | --- | --- | --- | --- |
| S1 | Command catalog | Connection details, tab "Commands" | connection manager | Switch commands on for a site and set the hard limits |
| S2 | Autonomy policy | Logic details, section "Autonomous control" | `control.manage` | Choose which commands a logic may send, within the catalog |
| S3 | Control activity | Operations, tab "Control" | anyone who can read control | Every attempt, with its honest status and a reason |
| S4 | Control status strip | Header of S2, S3 and the logic page | everyone | Kill switch, deployment switch, sink in use |
| S5 | Action detail | Opens from S3 (details pane) | operator | One attempt end to end, with verify and resolve |

No screen is added for the simulator itself. It is a service, configured by the same Connections page as any adapter. Fault
injection stays an API for tests (see section 8).

## 3. S1 Command catalog (Connection details, "Commands")

Purpose: an engineer states what may be commanded at this site and how far. Nothing is on by default.

```text
Connection: Simulator (local)                                  [Overview] [Assets] [Commands]
+-------------------------------------------------------------------------------------------+
| Commands                                              Version 3 · changed by A. Lee · 2d ago |
| Nothing here is sent until it is switched on and a logic's policy allows it.                |
|                                                                                            |
|  Command         Quantity   Range allowed        Max step   Verify after   Deadline   On   |
|  Set pump speed  speed spm  [ 0 ] to [ 12 ] spm  [ 2 ]      [ 2 ] s        [ 30 ] s   (o)  |
|  Run or stop     state      0 or 1               -          [ 2 ] s        [ 30 ] s   ( )  |
|                                                                                            |
|  Why are you changing this?  [______________________________]                              |
|                                                       [Cancel]            [Save changes]   |
+-------------------------------------------------------------------------------------------+
```

- A row is the adapter's command definition (read-only name, quantity, unit) plus the site's numbers (editable).
- An empty limit disables the switch and says why in the row ("Set a range to turn this on"). The framework has no default.
- Verify-after and deadline show the adapter's bounds as helper text ("2 to 60 s"); out-of-bounds is refused inline.
- Save needs a reason (printable, at most 1024 characters). One save is one audit record with before and after.
- A stale version (someone else saved) shows one banner, "This changed while you were editing. Reload to see it", and keeps
  the person's edits visible, never silently overwritten.
- Read-only users see the same table without inputs and no Save.
- Phone: each command becomes a stacked card; the table is never scrolled sideways.

## 4. S2 Autonomy policy (Logic details, "Autonomous control")

Purpose: say what this logic may send on its own. Builds on the current `AutonomyPolicyPanel`.

```text
Autonomous control                                                       Sink: Simulator
+-------------------------------------------------------------------------------------------+
| [x] Allow this logic to command equipment                                                   |
|                                                                                            |
|  Command (from the logic)   Value range            Site ceiling      Max step              |
|  [ Set pump speed       v]  [ 3 ] to [ 10 ] spm    0 to 12 spm       [ 1 ]        [Remove]  |
|  [+ Add command]            only commands this logic declares and the site allows          |
|                                                                                            |
|  At most [ 4 ] commands every [ 60 ] seconds                                               |
|                                              [Cancel]                    [Save policy]     |
+-------------------------------------------------------------------------------------------+
```

- The command picker lists only commands the active revision **declares** (`commandRequirements`) **and** the catalog
  **enables** for the logic's targets. The unit is filled in and fixed. An empty picker explains which side is missing:
  "This logic declares no commands" or "The site has not switched this command on" with a link to S1.
- "Site ceiling" is read-only text beside the inputs. A value outside it is refused inline and again by the server.
- A revision change that drops a permitted command shows one warning on this panel: "Set pump speed is no longer declared
  by the active revision." with the row marked, never silently removed.
- Sink is shown, not chosen, until the owner decides the dry-run and provider options (F9). Choosing is a later, separate row.
- Saving replaces the policy as a whole (as today). Cancel keeps the saved policy.

## 5. S3 Control activity (Operations, "Control")

Purpose: a plain history of what the platform did and what it can prove.

```text
Control                                                              [Filters] [search...]
+-------------------------------------------------------------------------------------------+
| Status        When          Logic          Command            Value   What we know        |
| Verified      16:02         Pump optimiser Set pump speed     7 spm   Read back 7 spm      |
| Needs review  15:48         Pump optimiser Set pump speed     9 spm   Sent; value not seen |
| Rejected      15:30         Pump optimiser Set pump speed     99 spm  Provider refused:    |
|                                                                       value out of range   |
| Stopped       15:12         Pump optimiser Set pump speed     8 spm   Kill switch on       |
+-------------------------------------------------------------------------------------------+
```

- Shared table: Status first, whole row opens S5, selected row highlighted, name stays a button.
- Status vocabulary is closed and each has one sentence in plain words:

| Status | Meaning shown to the person |
| --- | --- |
| Requested | Waiting to be sent. |
| Sent | The provider took it. We have not seen the value change yet. |
| Verified | We read the value back and it matches. |
| Needs review | We cannot tell whether it took effect. Check, then resolve. |
| Rejected | The provider refused it. Nothing changed. |
| Stopped | Not sent: a switch or limit stopped it. The reason is named. |
| Dry run | Recorded only. Nothing was sent. |

- "Sent" is never styled as success. Only Verified is positive; Needs review is the one attention colour.
- Filters: status, logic, command, date range. Result count and chips follow `FilterToolbar`.

## 6. S5 Action detail (details pane)

```text
Set pump speed to 7 spm                                                              [x]
Needs review · asked 15:48 · sent 15:48 · deadline 15:48:30
-------------------------------------------------------------------------------------
What happened     1  Recommendation approved by A. Lee
                  2  Checked: policy, site limits, kill switch
                  3  Sent to the Simulator (request r-9f2…)
                  4  Provider said: accepted
                  5  Read back 5.5 spm after 30 s: does not match 7
What to do        [Read back again]                          [Resolve as ...]
-------------------------------------------------------------------------------------
Details           Connection · Catalog version 3 · Policy version 2 · Sink: Simulator
```

- A timeline, oldest first, one line per fact the platform recorded; nothing inferred.
- "Read back again" is secondary. "Resolve as applied / not applied" needs a note and records the person.
- One card with one frame (`ui-conventions.md`); on phones it replaces the list.

## 7. S4 Control status strip

One line under the page header on S2, S3 and the logic page:

`Autonomous control: on · Kill switch: off · Commands go to: Simulator`

- Any abnormal state becomes the only thing on the line with one action: "Kill switch is ON. No command will be sent.
  [Release...]" (permission-gated, as today in `KillSwitchPanel`).
- The text that says which adapters can be commanded comes from `Control:Providers:AllowedAdapters`, so it cannot go stale.

## 8. Using the simulator

- Local and CI: the simulator adapter is enabled by default and is the only adapter allowed to send (`AllowedAdapters`).
- The screens above are checked against it in this order: S1 (enable SET_SPEED, range 0 to 12), S2 (policy 3 to 10),
  approve a recommendation, S3 shows Verified; then the faults: reject (Rejected), fail before applying (Needs review),
  slow apply (Sent, then Verified after the settle time), wrong value (Needs review), no read-back (Needs review at deadline).
- Fault injection is an API for tests and demonstration, not a screen, because a fault switch in the product UI would be a
  way to fake a result. If a demonstration panel is wanted it is dev-only and not built until you ask.

## 9. Behaviour common to all screens

- **States:** each screen defines loading (shared indicator), empty (one sentence and the next step), error (one `alert`
  with Retry), and read-only. No blank panels, no spinners without text.
- **Keyboard and screen reader:** every control is reachable in reading order, errors are announced, icon buttons have
  names, status is never colour alone (text always accompanies the badge).
- **Motion:** none beyond the shared loading indicator.
- **Responsive:** follows the breakpoints in `ui-conventions.md`; tables stack on phones; no nested scrollers.
- **Copy:** all strings in the localization file, keys `catalog.*`, `autonomyPolicy.*`, `controlActivity.*`; the status
  vocabulary above is the single source for both labels and sentences.

## 10. How this is kept honest

- Each screen ships with its structural test in `src/web/tests/` (headings, one primary button, labels, states) and a
  keyboard test, like the existing `autonomy-policy` and `control-actions` tests.
- A test pins the status vocabulary to the server's status enum so a new server status cannot appear unlabelled.
- This document is updated in the pull request that changes a screen; reviewers check the wireframe against the build.
- Screenshots of each screen against the simulator are attached to the F8 pull requests once the screens exist.

## 11. Decisions for the owner

1. Is "Needs review" the right word for Ambiguous, or do operators say something else?
2. Should S1 live as a tab on the connection, or as its own page under Control?
3. Is a dev-only fault panel for demonstrations wanted, or is the API enough?
4. Any fields to add to the S5 timeline that operators ask for first (for example well name, shift, or ticket number)?
