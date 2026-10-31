# 3. Recommendations inbox and lifecycle

## Purpose
One place to triage what the system suggests and to take each suggestion through approve or reject, execute, verify and resolve, with the evidence on screen. Plan: E2 (done), E4 (resolution), E8 (expiry, supersede, de-duplication, notifications).

Today: `RecommendationsPage` (inbox table, filters, decision review panel with approve/reject, Autonomous control panel). This design extends the detail with the post-approval stages.

## Who
- Read inbox and detail: `recommendations.read`.
- Approve, reject: `recommendations.decide` (scoped).
- Execute (send to gate/simulator) is system-driven after approval; **Retry execution** and **Resolve unknown outcome** need `control.manage`.
- Verify (confirm the effect from later data) is automatic; a manual **Mark as verified** needs `recommendations.decide`.

## Entry points
`#recommendations`, `#recommendations/{id}`; Overview "N recommendations are waiting for a decision"; asset detail > Recommendations; run detail > "Recommendation produced"; notification links (E8).

## Lifecycle
```
Proposed ─► Awaiting decision ─► Approved ─► Gate ─► Sent ─► Verifying ─► Resolved
                │                    │         │        │          │
                ├► Rejected          │         ├► Blocked         ├► Not verified (needs attention)
                ├► Expired           │         └► Unknown outcome ─► Resolved by person
                └► Superseded        └► Cancelled (kill switch)
```
Statuses are shown with `Status` (word + shape). Terminal: Resolved, Rejected, Expired, Superseded, Cancelled.

## Layout
`split-layout`: list 2 : detail 3.

```
┌ Recommendations ─────────────────────────┐ ┌ Reduce pump speed – Well 12 ────────── ✕ ┐
│ [Search…] [Filters (2)]  ● Live          │ │ ● Awaiting decision · expires in 38 min  │
│ Awaiting 4 · Approved 1 · Needs attn 2   │ │ Suggested: 42 Hz → 38 Hz (−4 Hz)         │
│ Status    Suggestion       Asset   Age   │ │ Why                                      │
│ ● Await   Reduce speed     W12     6 m   │ │  Fillage 62 % < 70 % limit (read 12:00:21│
│ ⚠ Unknown Reduce speed     W03     2 h   │ │  from CygNet, 4 s old)   [Open run →]    │
│ ○ Expired Raise setpoint   W07     1 d   │ │ Evidence  ▁▂▃▃▂▁  current vs limit       │
│                                          │ │ Execution mode  Recommendation           │
│                                          │ │ Gate preview  ● Would be allowed         │
│                                          │ │   in bounds 20–55 · 1 of 3 this hour     │
│                                          │ │ Sink  SIMULATOR                          │
│                                          │ │ Reason (required to reject) [        ]   │
│                                          │ │ [Approve recommendation] [Reject]        │
│                                          │ │ Timeline                                 │
│                                          │ │  12:00 Proposed · 12:01 Seen by Ana …    │
└──────────────────────────────────────────┘ └──────────────────────────────────────────┘
```

### Stage-specific detail
| Stage | Primary content | Actions |
|---|---|---|
| Awaiting decision | what, why, evidence with data age, gate preview, expiry countdown | Approve recommendation, Reject (reason required) |
| Approved | who/when, "Waiting for the policy gate" | none (Cancel while queued, `recommendations.decide`) |
| Gate blocked | gate reason in words and code, link to policy | Open autonomy policy; Re-propose if still valid |
| Sent | command, value, sink, idempotency key (technical), readback progress | none |
| Verifying | expected effect and the check ("fillage ≥ 70 % within 30 min") with progress | Mark as verified |
| Unknown outcome | readback expected vs actual, timeline | Resolve… (confirm applied / not applied + note) |
| Resolved | outcome, verified by (system/person), notes | none |
| Rejected / Expired / Superseded | reason and link to the superseding recommendation | none |

Approving requires explicit decision, never pre-selected. If the gate preview would block, Approve stays available but states the consequence: "Approving will be blocked by the policy: above maximum 55 Hz."

### Inbox behaviours (E8)
- De-duplication: identical open suggestions collapse into one row with "Seen 5 times, latest 12:00" (details lists occurrences).
- Supersede: a newer recommendation for the same asset/command marks the older "Superseded" and removes it from Awaiting counts.
- Expiry: countdown in detail and relative age in list; expired rows cannot be approved (button replaced by "Expired" text with [Re-run logic]).
- Bulk: select several awaiting rows and Reject with a shared reason; no bulk approve in the PoC.

## States
| State | Behaviour |
|---|---|
| Loading | `LoadingIndicator` in list; detail pane "Select a recommendation to review." |
| Empty | "No recommendations yet. Recommendations appear when logic running in recommendation or autonomous mode finds something to do." |
| Empty (filtered) | "No recommendations match." [Clear filters] |
| Error | existing codes: access denied, invalid response, unavailable; `role="alert"` with Retry |
| Stale evidence | If the data used is older than the module's allowed age at decision time: warning "The data behind this is 9 min old. Check the current value before approving." and Approve needs an extra confirm checkbox |
| Decision conflict | "Already decided by Ana at 12:02." detail refreshes, buttons disappear |
| Decision failure | inline error; stays in Awaiting; nothing partial |

## Real-time
- `recommendations`: refetch list, counts and open detail; new items briefly highlight (no animation under reduced motion) and the tab title shows the awaiting count.
- `control`: refetch gate/sent/unknown stages (they are produced by the control pipeline) and the kill switch state shown in the page.
- `executions`: refresh evidence links when the producing run changes.
- Open detail never replaces text typed in the reason field; a decided-elsewhere banner appears instead.
- Polling fallback as in README.

## Copy
"Approve recommendation", "Reject recommendation", "Reason for rejecting", "Expires in 38 min", "Waiting for the policy gate", "Blocked by the policy gate", "Outcome unknown", "Resolve outcome", "Mark as verified". Status names: Awaiting decision, Approved, Sent, Verifying, Resolved, Rejected, Expired, Superseded, Blocked, Outcome unknown.

## Accessibility
- Filter chips and counts are buttons with names like "Awaiting decision, 4".
- New arrivals announced politely: "1 new recommendation awaiting decision" (debounced, once per batch).
- Expiry countdown is not a live region; the expired state change is announced.
- Reject reason: required, `aria-required`, error tied by `aria-describedby`.
- Timeline is an ordered list with `<time>`.
- Approve/Reject buttons are never adjacent identical sizes without text difference; Reject is `secondary`, Approve `primary`; destructive confirmation not needed because reject is reversible only via re-propose (stated in copy).

## Open questions
1. Approve and execute: one action (approval immediately triggers the gate, as in E2) or a separate "Execute" step for operators?
2. Who may verify manually and does verification need evidence?
3. Notification channel for E8 (in-app only, email, SCADA alarm)?
4. How long is the default expiry, and per module or per policy?
5. Do rejected recommendations feed back into the module's suppression (for example snooze for 24 h)?
6. Should "needs attention" (unknown outcome, not verified) be separate from the inbox, for example a banner on the Overview?
