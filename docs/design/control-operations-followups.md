# Control operations: follow-up designs for discussion

Status: **proposed, for discussion. Nothing here is built.** Owner decisions so far: manifest v2 is extended with declared
commands (section 3), Control history starts as a tab (section 4), and both pause and cadence editing are needed (section 5). It comes from a review of the control work merged on `develop`
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
- Each row shows its **age** ("sent 3 h ago"). Whether a row is *overdue* is not a platform-wide constant: it is judged
  against how long **that command** was allowed to take to confirm. The command catalog in `provider-command-seam.md` (4.2,
  4.5) gives every command a settle time and a verification deadline, and the action records `verifyDeadlineUtc` when it is
  dispatched. A row past its own deadline is shown as overdue. Until the catalog exists (the simulator), rows show age only
  and no overdue treatment, rather than inventing a number.
- The `control` change area already exists, so the panel refreshes on signal; no new signal.

**Where it appears.** Today only on the Recommendations page. Operators who never open it would miss it, so the Overview
"needs attention" strip gets one more item, "N commands awaiting a decision", shown to anyone who can read recommendations
and linking to the panel. This reuses the existing Overview summary pattern; it does not add a page.

**Recommendation.** Build the server filter, the oldest-first ordering with age, and the Overview item as one small change
(server plus web), with a test that seeds more than 200 newer actions and asserts the old open one is still returned.

**Decision (settled in discussion).** No fixed 15-minute warning. An earlier draft of this section proposed one; that number
had no basis in the code. Freshness limits in this product belong to the logic: a module's own parameter says how old a
real-time input may be. Two existing, different limits are worth keeping apart:

- the **input freshness limit** (module parameter): how old a measurement the logic may use;
- the **approved-recommendation age limit** (`Control:Autonomous:ApprovedRecommendationMaxAge`, a global 30 minutes today):
  how old an approved recommendation may be when a person executes it.

Open question for discussion: should the second one also come from the module (for example derived from the module's
declared input freshness limit, with the global value as the ceiling), so a fast process is not held to the same limit as a
slow one? That is a separate change to the gate and is not needed for this section.

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

**Decision (settled in discussion).** Extend manifest v2 with declared commands (option 2).

Proposed shape, for review with the module contract owners:

- `controlCommands`: optional array, 0 to 64 entries, next to `outputRequirements`, each with `name` (unique within the
  manifest, case-sensitive, 1 to 256 characters, never the reserved advisory name), `quantity`, optional `unit`, and
  optional `suggestedRange` (`min`, `max`) that is advice for the policy editor, never a limit.
- Registration validates the list the way it validates output requirements (bounded, unique ids, no reserved names).
- **Policy editor:** offers exactly the declared commands for the selected revision's module, pre-fills the unit, shows the
  suggested range as a hint. Free text is removed, so case mistakes cannot happen. A policy saved for a command the active
  revision's module does not declare is rejected by the server (and a revision change that drops a permitted command shows a
  warning on the policy).
- **Materialisation:** a control intent naming an undeclared command is rejected with a clear reason rather than becoming a
  recommendation that can never run.
- **Provider catalog:** entries reference declared names, so there is one vocabulary.
- **Compatibility:** modules that declare nothing keep working as advisory-only (they cannot be given a policy), which is
  already how an empty list behaves. Whether this is manifest 2.1 (additive, optional field) or a new major version is for
  the contract owners; additive and optional is the recommendation, because older readers can ignore the field.
- The server also rejects duplicate commands in a policy (case-insensitive) regardless of the manifest.

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

**Control history as a tab (settled in discussion).** The Recommendations page gets a second tab beside the inbox, using
the same tab pattern as Logic (Your logic / Schedules / Modules), so no navigation item is added. The tab is a read-only list
of every control action (all statuses) with `FilterToolbar` filters (status, configuration, asset, date range) and the
shared master-detail layout. The detail pane shows what we already store: who or what acted, the gate code in words, command
and value, sink and reference, readback value and time, and the resolution note. It is the screen operators and auditors need
after an incident, and it replaces searching the Activity feed. It needs the status filter and capability fields from
sections 1 and 2, date filters, and cursor paging in place of today's hard 200 window. We start with that and refine from use.

```text
Recommendations                                   [ Inbox ] [ Control history ]
+--------------------------------------------------------------------------------+
| Filters (3)  status: Ambiguous x  config: Pump watch x            12 results   |
| Status      Command      Value   Asset      Sent            Result             |
| Ambiguous   SET_CHOKE    40 %    Well 7     12:01 (3 h)     Result unknown     |
| Verified    SET_CHOKE    38 %    Well 7     09:00           Target confirmed   |
+--------------------------------------------------------------------------------+
```

**Recommendation.** Build sections 1 to 3 first, then the history tab.

## 5. Schedules: pause, resume and change frequency

Both pausing and changing frequency are needed (settled in discussion). This section is the design for both together.

### 5.1 What the code does today (verified)

- A schedule (`enabled`, `cadenceSeconds`, `startAtUtc`, `timeZoneId`) is part of an immutable configuration revision.
- `ScheduledShadowSchedulerService` reads the **active revision's** schedule. Occurrences are counted from `startAtUtc`
  (`ScheduleOccurrences`), and only the **latest** due occurrence is dispatched per sweep, so there is no catch-up burst.
- Duplicate protection is `(revision id, scheduled-for time)`; an overlapping active run on the same assets is skipped and
  recorded.
- A run's execution snapshot is built from the revision, so it records the revision's schedule, not any later change.

### 5.2 Why not clone a revision for every change

Cloning the active revision with a new schedule keeps one source of truth, but it makes the most urgent operator action
(stop it now) a save, validate and activate sequence, fills the revision list with one-line changes, and tangles a harmless
frequency tweak with the module and parameter history. It stays right for changes to *what* runs; it is wrong for *when*.

### 5.3 Design: a live schedule beside the revision

A small per-configuration **schedule state** controls when the active revision runs. The revision keeps its schedule as the
*declared* intent; the state is the *live* value.

```text
ConfiguredLogicScheduleState                 ConfiguredLogicScheduleChange (append-only)
  configuredLogicId  (key)                     id, configuredLogicId, version
  version            (int, +1 per change)      kind: Activated | Paused | Resumed | CadenceChanged
  enabled            (false = paused)          before / after (enabled, cadence, anchor)
  cadenceSeconds                               actor, reason (optional, 1024), changedAtUtc
  anchorUtc          (start of the grid)
  timeZoneId
  changedBy, changedAtUtc, reason
```

Rules:

1. **Effective schedule.** If a state row exists the scheduler uses it; if not, it uses the active revision's schedule
   (lazy fallback, so no data migration is needed and rollout is safe). The first change writes the state row.
2. **Activating a revision resets the live schedule to that revision's declared schedule** and writes an `Activated` change.
   Otherwise an engineer activating revision 4 with a 5 minute schedule would silently keep an operator's old 1 minute value.
   The UI shows "differs from revision N" whenever the live value is not the declared one.
3. **Anchor.** Any cadence change or resume sets `anchorUtc = now` and the first occurrence is `anchor + cadence`. This keeps
   the occurrence grid clean, prevents an immediate catch-up run on resume, and means a changed cadence can never produce two
   runs inside one interval. `(revision, scheduled-for)` de-duplication keeps working because identical instants still map to
   one run.
4. **Pause is stop-only.** Pausing prevents new scheduled dispatch. It does not cancel a run already in flight, and manual
   Shadow runs stay available. Like the kill switch, a stop-only action needs no confirmation; **resume asks for one**, and
   shows the next run time. For Recommendation and Autonomous configurations, pausing stops new runs that would create
   recommendations or control actions; it complements the kill switch (which stops commands, not the logic).
5. **Minimum cadence.** The floor is the largest of the platform default (15 s, decided in gap 19) and every adapter minimum
   declared by the connections the configuration uses (`AdapterDescriptor` gains `MinimumScheduleSeconds`, default none).
   A change below the floor is rejected on save with the floor named in the message. If a floor later rises above a live
   cadence (an adapter update), the scheduler does not silently clamp: it skips with a recorded reason
   (`SCHEDULER_CADENCE_BELOW_MINIMUM`), and the Schedules tab shows the warning.
6. **Audit trail on runs.** The execution snapshot records `scheduleVersion` and the cadence in force, so a run can always be
   tied to the schedule that triggered it.
7. **Concurrency.** Changes carry the expected version (`If-Match`, as activation already does); a stale edit is a conflict
   with the current value shown.

### 5.4 API

| Route | Purpose | Permission |
| --- | --- | --- |
| `GET /configured-logic/{id}/schedule` | State, declared schedule, floor, next run (server computed), recent changes (paged) | `configured-logic.read` |
| `POST /configured-logic/{id}/schedule/pause` | `{ reason? }`, requires `If-Match` | `configured-logic.manage` |
| `POST /configured-logic/{id}/schedule/resume` | `{ reason? }`, requires `If-Match` | `configured-logic.manage` |
| `PUT /configured-logic/{id}/schedule` | `{ cadenceSeconds, reason? }`, requires `If-Match`; time zone stays as declared | `configured-logic.manage` |

Each is also scoped by the configuration's assets (same check as the other configured-logic writes), emits the live-update
signal for the configured-logic and a new `schedules` area, and is added to the API authorisation inventory. Separate
pause and resume routes (rather than one `enabled` flag) keep the intent explicit in audit and make the stop-only action easy
to reason about. The permission is the existing `configured-logic.manage`, as gap 19 decided.

### 5.5 Screens

```text
Logic > Schedules
+----------------------------------------------------------------------------------+
| Logic         State        Frequency    Next run     Last run        Actions     |
| Pump watch    Running      Every 1 min  12:04:00     12:03:00 Done   [Pause] [..] |
| Tank check    Paused       (Every 5 m)  --           11:10:00 Done   [Resume]    |
|               by A. Rivera, 11:42 "Sensor swap"                                  |
| Choke opt.    Below min    Every 10 s   --           --              [Change]    |
+----------------------------------------------------------------------------------+
Change frequency (inline panel, opens under the row)
  Every [ 5 ] [ minutes v ]   Minimum for this logic: 15 seconds (ForeSite)
  Next run after saving: 12:09:30            [Cancel] [Save frequency]
```

- Pause, Resume and Change frequency show only for people with `configured-logic.manage` on that configuration. Resume uses
  the same confirmation pattern as the kill switch release.
- The logic's **Overview** tab (see `logic-details-refinement.md`) shows the same state, the declared schedule from the saved
  revision, a "Schedule control" block with Pause or Resume and Change frequency, and a collapsed "Schedule changes" list. No new
  tab and no new page.
- The asset page's schedule summary (gap 19, part b) later reuses the same component and the same server fields.

### 5.5.1 Fit with the logic details refinement

The logic detail now has Overview, Configuration and Activity sections, built on principles this design must respect:

- **Saved revisions stay immutable; live observations never replace them.** The live schedule is a separate projection beside
  the revision snapshot, and the screen shows both ("declared by revision N" next to the live value). Pause and cadence changes
  never create or edit a revision.
- **Mutation is blocked when the reviewed revision is not the live one**, and while a revision is being edited (tabs are disabled
  then). The schedule actions follow the same `mutationBlocked` rule, and the server's version check covers the rest.
- **Reuse, do not duplicate.** Next run is already read from the server's activity projection on the Overview and from the asset
  logic summary, plus the new `NextRunAtUtc` on the configuration list. Today each calls `ScheduleOccurrences.NextAfter` itself.
  Before the live state exists they must share one `EffectiveSchedule` resolver (revision schedule plus live state, used by the
  scheduler too), otherwise a paused schedule would still show a next run in three places.
- **Progressive disclosure.** The change list is collapsed on the Overview; Activity keeps meaning run history.
- **Existing components.** ActionButton, Status, the confirmation pattern of the kill switch release, and DetailTabs; no placeholder
  readiness or verdict is added.
- The hint "Schedule times are shown in your local time zone" stays; the Schedules tab text "To change a schedule, save a new
  revision" is replaced when the actions land, because it would then be wrong.

### 5.6 Delivery

1. Server: state and change tables, one `EffectiveSchedule` resolver used by the scheduler and by every next-run read, activation resets it, execution snapshot records
   the version, `GET` plus pause and resume; tests for the anchor rule, no catch-up burst, activation reset, and de-dupe.
2. Web: state column, Pause and Resume on the Schedules tab, history in the configuration detail.
3. Cadence change, with the adapter-minimum field on `AdapterDescriptor` and the floor message.

Open points for discussion: whether Resume should offer "run once now" (manual Shadow runs already cover it); and whether
`reason` should be required for pause during a pilot, as it is for resolving an in-doubt command.

### 5.7 Adapter minimum frequency (delivery step 3)

**Status: delivered** in #398 (descriptor field, cache, scheduler skip) and #399 (control service, contract, screens). The execution
snapshot does not yet record the schedule version (5.6 step 1 note); it is the one deliberate gap left in 5.6.

Pause, resume and frequency change are delivered (#394, #395, #396). The server-wide bounds (15 seconds to 7 days) are
the only floor today. A provider can need a slower floor than that, and the logic's own adapter is what knows it.

- **Where it lives.** `AdapterDescriptor.MinimumScheduleSeconds` (int, default 0 = no adapter-specific floor), owned by the
  adapter beside its other facts, so the Server stays provider-neutral. Raised only with a stated reason in the adapter.
- **Which adapter a logic uses.** The module manifest's `RequiredAdapterKey`, already part of the immutable revision
  snapshot. A module with none has no adapter floor.
- **Server knowledge.** The Server reads descriptors through `ConnectionService.ListAdaptersAsync` (the gateway). A small
  cached catalog port (`IAdapterScheduleFloors`) holds the last successful answer, refreshed on a timer and on demand.
  The scheduler and the control service read the cache; neither calls the gateway on its own path.
- **Changing frequency.** Below the effective floor (`max(15 s, adapter floor)`) the request is refused with a stated floor.
  If the adapter's floor is not yet known the request is accepted against the global floor and flagged, because refusing
  would make a gateway outage block a person from slowing a schedule down.
- **Dispatch.** A due occurrence on a schedule faster than the floor is not dispatched; the decision is recorded as
  `SCHEDULER_CADENCE_BELOW_MINIMUM` with the floor, so it shows in the scheduler's decisions like every other skip. Unknown
  floor dispatches (the global floor already applies) and is logged once per adapter.
- **Why skip and not clamp.** Silently running at another cadence would hide that the saved schedule cannot be honoured.
  A revision that declares a faster cadence than its adapter allows keeps its declaration and is shown as **Below
  minimum**, with the floor, in the Schedules list and the Overview schedule block.
- **Contract.** `ConfiguredLogicScheduleControlResponse` gains `MinimumCadenceSeconds` per logic (the effective floor) in
  place of the global constant, and `belowMinimum`; the list response carries `belowMinimum`.

Steps: (1) descriptor field, cache port, scheduler skip and tests; (2) control service floor and contract; (3) web.

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
| 1 | Status filter, oldest-first with age, capability on the response, Overview item (sections 1 and 2) — done | none |
| 2 | Policy duplicate check on the server (section 3) — done | none |
| 3 | Server-provided next run — done; last run still needs the decision on exposing run status (section 6) | decision on exposing run status |
| 4 | Pause and resume (section 5.6 steps 1 and 2) — done (#389 to #396) | none |
| 5 | Manifest `controlCommands` and the policy editor using it (section 3) | contract owners' review of the shape |
| 6 | Cadence change with adapter minimums (section 5.6 step 3) — done (#397 to #399); no adapter sets a floor yet | provider-stated limits from the owner |
| 7 | Control history tab (section 4) | none |
| 8 | Operator runbook (section 6) — done, `docs/implementation/control-operator-runbook.md` | none |

Steps 1 and 2 are small enough to merge on their own with CI. Steps 4 to 7 change shared contracts or add screens, so each
gets its own pull request, and the manifest and schedule-state changes (steps 4 and 5) are reviewed as contracts first.
