# Control operations: follow-up designs for discussion

Status: **proposed, for discussion. Nothing here is built.** It comes from a review of the control work merged on `develop`
(Execute, kill switch, autonomy policy editor, commands awaiting a decision, Schedules tab) and turns each finding into a
design that fits how the app already works (`docs/design/ui-conventions.md`, the scoped-capability pattern, the live-update
signal). Each section ends with a recommendation and the decision it needs.

Each item was checked against the code unless it says otherwise. What the PoC branch does is reference only.

## 1. Commands in doubt must never be hidden

**Problem (verified).** The "commands awaiting a decision" panel calls `GET /control/actions?limit=200` and filters
`Dispatched`/`Ambiguous` in the browser. The server returns the newest 200 actions the caller may read
(`ListActionsAuthorizedAsync`, ordered by `CreatedAtUtc` descending). A command that is still in doubt but older than the
newest 200 actions is not shown, and a command in doubt is exactly the thing that must not disappear. The same window also
affects any count we would put on another page.

**Design.**
- Server: `GET /control/actions` gains `status` (repeatable, validated against the status names) and the repository applies it
  in SQL before the limit, with scope already applied as today. New `GET /control/actions/open-count` is not needed: the
  panel asks for `status=Dispatched&status=Ambiguous&limit=200` and shows "200+ open" if the page is full, which fails loud
  rather than quiet.
- The inbox panel uses the filtered call. Ordering for this view is **oldest first**: the longest-standing doubt is the most
  urgent.
- Each row shows its **age** ("sent 3 h ago") and moves to a warning treatment after a bound that is configuration
  (`Control:Autonomous:InDoubtWarningAfter`, default 15 minutes), so a stale doubt cannot look like a fresh one.
- The `control` change area already exists, so the panel refreshes on signal; no new signal.

**Where it appears.** Today only on the Recommendations page. Operators who never open it would miss it, so the Overview
"needs attention" strip gets one more item, "N commands awaiting a decision", shown to anyone who can read recommendations
and linking to the panel. This reuses the existing Overview summary pattern; it does not add a page.

**Recommendation.** Build the server filter, the oldest-first ordering with age, and the Overview item as one small change
(server plus web), with a test that seeds more than 200 newer actions and asserts the old open one is still returned.

**Decision.** Is 15 minutes the right point for the warning treatment?

## 2. Show a person only the buttons that will work

**Problem (verified).** Verify and Resolve show for anyone holding `control.manage`. The server also requires that the
caller can decide the recommendation on its Assets (`RequireDecidableControlActionAsync`), and answers "not found" for both
missing and forbidden. A caller with the permission but not the scope sees buttons that fail with a misleading message.

**Design.** Follow the pattern recommendations already use: the response carries a computed capability
(`RecommendationSummaryResponse.CanDecide`, filled by `GetRecommendationActionsAsync`). `ControlActionResponse` gains
`CanManage`, computed with the same scope query for the page of actions being returned (one query, not one per row). The web
shows Verify and Resolve only when `canManage` is true and, when the permission is held but the capability is not, shows
the row read-only with a short note ("You can see this command but not change it"). The error text for a lost race is
changed to say the command was already settled or is no longer available to the person.

**Recommendation.** Build with section 1 since both change the same response and query.

## 3. Policy commands: say what a module can emit, and make matching unsurprising

**Problem (verified).** The gate matches policy commands with ordinal, case-sensitive equality and takes the first match;
the server accepts duplicates and the editor is free text. A policy entry that differs in case from what the module emits
refuses every command with "not permitted", and the person has no way to know why. Module manifests declare input and output
requirements (`outputRequirements`) but **not the commands a module may ask for**, so nothing can suggest them.

**Design options.**
1. *Free text with guardrails (smallest).* The server rejects duplicate commands case-insensitively; the editor tells people
   commands are case-sensitive and shows the commands seen in this configuration's recent recommendations as suggestions.
2. *Declared commands (recommended).* Manifest v2 gains an optional `controlCommands` list (`name`, `quantity`, `unit`,
   optional suggested range). Registration validates it. The policy editor offers exactly those commands and pre-fills unit.
   At materialisation, a control intent naming an undeclared command is rejected with a clear reason instead of becoming a
   recommendation that can never run. The provider command catalog in `provider-command-seam.md` (4.2) can then reference
   declared names rather than inventing a second vocabulary.

Option 2 is a module-contract change and so needs a manifest version decision and the SDK owner, but it also removes a whole
class of silent misconfiguration. Option 1 can ship first and is compatible with option 2.

**Recommendation.** Ship option 1 now (small, server and web); raise option 2 with the module contract owners.

**Decision.** Do we extend manifest v2 with declared commands, or add a v3?

## 4. Where control lives in the app

Today the kill switch, the commands awaiting a decision and Execute are on the Recommendations page; the policy is in the
Logic workspace; the Schedules tab is under Logic. That fits where each task is done, but there is no place to answer "what
did the platform send, and when?".

```text
Recommendations page (operators)            Logic > <configuration> (engineers)
+---------------------------------------+   +---------------------------------------+
| Control kill switch        [Engaged]  |   | Revisions ...                         |
| Commands awaiting a decision   2 open |   | Autonomy policy        [Enabled]      |
|   SET_CHOKE 40 %  sent 3 h  [Verify]  |   |   SET_CHOKE  10 to 60 %               |
|                             [Resolve] |   |   3 actions / 600 s                   |
| Summary cards                         |   |   [Edit autonomy policy]              |
| Inbox list | Review / Execute pane    |   +---------------------------------------+
+---------------------------------------+
```

**Proposed addition: Control history.** A read-only list of every control action (all statuses) with filters (status,
configuration, asset, date), reachable from the kill switch panel and the Overview, using the shared `FilterToolbar` and the
master-detail layout. The detail pane shows the audit trail we already store: who/what acted, gate code in words, command
and value, sink and reference, readback and verification time, and the resolution note. It is the screen auditors and
operators need after an incident, and it replaces searching the Activity feed. It needs the same status filter and capability
fields as sections 1 and 2, plus date filters and cursor paging (the current endpoint has a hard 200 window).

**Recommendation.** Do sections 1 to 3 first, then Control history as its own design review (it needs paging and retention
decisions). Do not add a top-level navigation item until history exists; it would be an empty page.

**Decision.** Should Control history be a tab on the Recommendations page or its own section in the navigation?

## 5. Schedules: pause, resume and change frequency

**Problem.** The Schedules tab is read-only because a schedule belongs to a configuration **revision**, and revisions are
immutable once saved. Changing frequency therefore means saving a new revision.

**Options.**
1. *Revision-clone.* Edit-frequency clones the active revision with a new schedule, validates it and activates it (the
   existing validate/activate steps). Pro: no new concept and full audit by revision. Con: a one-line change creates a
   revision and needs the activate confirmation; pausing during an incident is heavier than it should be.
2. *Schedule outside revisions (recommended).* A small mutable `ConfiguredLogicSchedule` per configuration (enabled,
   cadence, start, time zone, changed by/at), read by the scheduler instead of the revision's schedule, with each change
   audited and signalled. Revisions keep a schedule as the *initial* value only. Pro: pause/resume is one audited action;
   cadence can be tuned without a new module revision. Con: a second source of truth that migration and the scheduler must
   handle (the scheduler currently reads the active revision's schedule), and the PoC probe flagged this trade-off.
3. *Pause only.* A per-configuration "paused" flag in the scheduler's own table; cadence changes still use revisions. Smallest
   safe step: it answers the urgent operator need (stop it now) without moving the schedule's source of truth.

The minimum cadence rule already decided in gap 19 (default 15 s, adapter-declared minimum, enforced on save and at dispatch)
applies to options 1 and 2; no adapter declares a minimum yet, which is its own small prerequisite.

**Recommendation.** Option 3 first (pause/resume, with `configured-logic.manage`, audited, live-signalled, shown in the
Schedules tab and on the asset page), then decide between 1 and 2 for cadence edits once the adapter minimum exists.

**Decision.** Is "pause" enough for the pilot, or is cadence editing needed first?

## 6. Smaller corrections

- **"Last run" accuracy.** The Schedules and configuration lists take the last run from the runs already loaded in the
  browser, so an older run reads "no recent run". Proposal: the configured-logic list response includes `lastRunAtUtc` and
  `lastRunStatus` computed in SQL, which also makes the column correct for people without `executions.read` only if we choose
  to expose it to them (a decision, because it reveals run status).
- **Clock.** Next run is computed in the browser from the browser clock. Proposal: return `nextRunAtUtc` from the server, using
  the existing `ScheduleOccurrences.NextAfter`, so there is one implementation. The browser copy and its mirror test can then
  be deleted.
- **Hard-coded "only the simulator" wording** appears in three messages. Proposal: one message key driven by whether any
  `Provider` sink is enabled, changed in the same pull request that first allows a `Provider` policy, with a test that fails
  if the two drift.
- **Operator documentation.** `execution-modes.md` describes the gate but not what a person does. Add an operator runbook:
  the kill switch, executing, what each gate message means, in-doubt commands (verify versus resolve and what to check on
  the equipment), and the policy fields.
- **Polling.** The kill switch and in-doubt panels each poll. Fold both into one shared control-state hook so a page makes one
  request cycle; low risk, low priority.

## 7. Suggested order

| Step | Change | Needs |
| --- | --- | --- |
| 1 | Status filter, oldest-first with age, capability on the response, Overview item (sections 1 and 2) | decision 1 |
| 2 | Policy duplicate check and suggestions from recent commands (section 3, option 1) | none |
| 3 | Server-provided next/last run (section 6) | decision on exposing run status |
| 4 | Pause/resume (section 5, option 3) | decision 5 |
| 5 | Operator runbook | none |
| 6 | Control history design review, then build (section 4) | decision 4 |
| 7 | Declared commands in the manifest (section 3, option 2) | module contract owners |

Items 1 and 2 are small enough to merge on their own with CI. Sections 3 option 2, 4 and 5 change shared contracts or add
screens, so they come back here before any code.
