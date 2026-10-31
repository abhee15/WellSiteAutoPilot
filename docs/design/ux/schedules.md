# 1. Schedules overview and in-context editor

## Purpose
Let an engineer see every configuration that runs on a timer, change its cadence safely, and pause or resume it, without leaving the configuration. Plan: F1–F4 (F6 later).

## Who
- View: `configured-logic.read`; last/next run times also need `executions.read` (otherwise shown as "—" with a hint).
- Edit, pause, resume: `configured-logic.manage` (decided, F3). Others see the controls disabled with the reason "You need permission to manage configured logic."

## Entry points
1. **Logic > Configurations > (configuration) > Schedule tab** (primary, in-context editor).
2. **Logic > Schedules** list tab (overview across configurations); row opens the same detail.
3. Overview page "Needs attention" item when a schedule is lagging or paused by the system.
4. Deep link `#logic/schedules/{configurationId}`.

## Layout
Overview (master) with detail pane (`split-layout`, 2:3). The Schedule tab in a configuration is the detail pane alone.

```
Logic   [Catalog] [Configurations] [Schedules] [Authoring]            ● Live
┌ Schedules ─────────────────────────────┐ ┌ Pump fillage low alert – Well 12 ─────── ✕ ┐
│ [Search schedules…] [Filters (1)]      │ │ Status: ● Running      [Pause schedule]    │
│ Active filter: Paused ✕   3 of 41      │ │                                            │
│ Status   Configuration  Every  Next    │ │ Schedule                                   │
│ ● Running Fillage – W12  15 s   in 9 s│ │  Run every     [ 30 ] [seconds ▾]          │
│ ⚠ Lagging Fillage – W14  15 s   late  │ │  Minimum for CygNet: 15 s (adapter limit)  │
│ ⏸ Paused  Amps – W03     1 min  —     │ │  If the previous run is still going        │
│ ○ Off     Pressure – W07 —      —     │ │   (•) Skip this run   ( ) Queue one run    │
│                                        │ │  Missed runs after downtime                │
│                                        │ │   (•) Run once now   ( ) Skip them         │
│                                        │ │  [Save schedule]  [Discard changes]        │
│                                        │ │ Activity                                   │
│                                        │ │  Last run  12:00:21  ● Completed  · 1.2 s  │
│                                        │ │  Next run  12:00:51  (in 9 s)              │
│                                        │ │  Last 20 runs  ▁▂▁▁▃▁▁▁  [Open runs →]     │
└────────────────────────────────────────┘ └────────────────────────────────────────────┘
```

Fields: run every (number + unit seconds/minutes/hours), overlap policy (skip default, queue one), missed-run catch-up (run once / skip), enabled state. Time zone, blackout windows and jitter (F6) are placeholders: a collapsed "Advanced (coming later)" is **not** shown in the PoC; do not display disabled future features.

### Minimum frequency
- Default minimum 15 s. Each adapter may declare a larger minimum (per-provider override); the effective minimum is the **largest** among the providers the configuration reads (for multi-provider modules, name the provider that sets it).
- The helper text under the field always states the effective minimum and why: "Minimum 15 s (default)" or "Minimum 60 s because ForeSite requires it".
- Validate on blur and on save; the server enforces again (save and dispatch). A server rejection shows inline against the field with the server's minimum, never a generic toast.

## States
| State | Behaviour |
|---|---|
| Loading | `LoadingIndicator` in the list; editor shows skeleton of fields; controls disabled. |
| Empty (no schedules) | "No schedules yet. Open a configuration and choose a run frequency to start scheduled runs." [Go to configurations] |
| Empty (filtered) | "No schedules match these filters." [Clear filters] |
| Not scheduled | Status "Off"; the editor shows "Run every" blank and primary action "Start schedule". |
| Paused | Status "Paused" with who paused and when; next run "—"; button becomes "Resume schedule". Pausing needs no confirmation; resuming shows "Next run in …". |
| Lagging / stale | Warning when last run is older than 2 × interval or next run is overdue: "This schedule is running late (last run 4 min ago, expected every 30 s)". Link to scheduler health docs. |
| System-paused | Paused automatically (for example connection unavailable): "Paused by the system: connection unavailable." with the reason code in technical text; Resume is allowed once the connection is healthy. |
| Error (load) | `role="alert"` "Schedules could not be loaded." [Retry]; last data remains, marked stale. |
| Error (save) | Inline field error plus summary at the top of the form; form state kept. |
| Conflict | Someone else changed it (revision mismatch): "This schedule was changed by Ana at 12:01. Review the new values." [Reload] [Keep my edits]. |
| Dirty | Navigating away shows "Discard unsaved schedule changes?". |

## Real-time
- `configured-logic` changes (schedule edits, pause/resume) refetch the list and detail. If the owner approves a dedicated area, use `schedules`.
- `executions` changes update last/next run and the sparkline of recent runs (refetch only the open schedule's latest runs).
- The next-run countdown is computed on the client from `nextRunUtc` (1 s tick, only for the visible row, paused when the tab is hidden); it is not re-fetched each second.
- If the hub is disconnected `LiveStatus` shows it and polling continues; countdown text switches to absolute time to avoid implying live accuracy.
- Edits in progress are never overwritten by a refetch; a banner offers "A newer version is available".

## Copy (sentence case)
- Heading: "Schedules". Fields: "Run every", "If the previous run is still going", "Skip this run", "Queue one run", "Missed runs after downtime".
- Buttons: "Save schedule", "Discard changes", "Pause schedule", "Resume schedule", "Start schedule".
- Errors: "Run every must be at least 15 seconds for this configuration." / "A schedule needs a number greater than zero."
- Pause confirmation toast (status region): "Schedule paused. No runs will start until you resume it."

## Accessibility
- The list is the shared table; row opens detail; name is a button.
- Number + unit are a labelled group (`role="group"`, one legend "Run every").
- Overlap and catch-up are radio groups with fieldset/legend.
- Pause/resume announces via `role="status"`; the button label changes, focus stays on it.
- The countdown is not a live region (would announce every second); only state changes (Running, Lagging) are announced.
- Minimum-frequency error uses `aria-describedby` and `aria-invalid`.

## Open questions
1. Own top-level route and change area, or only inside Logic?
2. Is "queue" limited to one pending run (assumed) or a bounded queue?
3. Should catch-up default to "run once" or "skip"? For shadow mode skip seems safer; for autonomous mode skip definitely.
4. Should pausing a configuration's schedule be audited with a mandatory reason?
5. How is the effective minimum shown when a configuration is rebound to a different provider after saving a faster cadence (auto-raise, or flag as invalid and pause)?
6. Do we show scheduler-wide health (lag, queue depth from `probe/poc-scheduler-load`) on this page or only in Status?
