# PoC UX designs (index)

Design only; nothing here changes behaviour. Sources: `docs/implementation/poc-master-plan.md` (A4, C2, D7, D9, E3, E4, F1–F4, G3, G4), `docs/design/ui-conventions.md`, and the current web app (`src/web/src`).

| # | Area | File | Plan items |
|---|---|---|---|
| 1 | Schedules overview and in-context editor | [schedules.md](schedules.md) | F1–F4, F6 |
| 2 | Autonomy policy editor and kill switch | [autonomy-control.md](autonomy-control.md) | E3, E4, E7, E9 |
| 3 | Recommendations inbox and lifecycle | [recommendations.md](recommendations.md) | E2, E4, E8 |
| 4 | Authoring workspace | [authoring.md](authoring.md) | D2–D4, D7, D9, C2 |
| 5 | Connection setup and point-map/UDC editor | [connections-point-map.md](connections-point-map.md) | A2, A4, A5 |
| 6 | Run detail | [run-detail.md](run-detail.md) | G4, E10 |

No static React prototype was added: none of these screens can be built from existing components without wiring to endpoints that do not exist yet (schedules, policies, point-map save, authoring test runner), and a non-wired page would drift from the real contract. Each wireframe names the existing components to reuse.

## Shared rules (apply to every screen)

**Components.** `ActionButton` (one `primary` per area), `CloseButton`, `SearchInput`, `FilterToolbar`, `Status`, `LoadingIndicator`, `EmptyRow`, `SummaryCard`, `PageHeader`, `LiveStatus`; master-detail uses `split-layout` with `useSplitPaneViewport`/`useSplitPaneFocus`. Tokens from `tokens.css` only; `rem` units; breakpoints in `em` range syntax. No nested scrollers.

**Copy.** Sentence case everywhere (guard #358), through `localization.ts` keys, never inline strings. Buttons are verbs ("Pause schedule"), never "OK". Errors say what failed and what to do. Reason codes (for example `CYGNET_QUANTITY_UNSUPPORTED`) are shown as a plain sentence with the code in a secondary technical line.

**Role-aware visibility (#355).** A screen hides what a user cannot read and disables (with a stated reason) what they can read but not change. Server remains the authority; the UI never infers security from the hash.

| Permission | Seen as |
|---|---|
| `executions.read` | runs, run detail, schedule last/next run |
| `configured-logic.read` / `.manage` | schedules and configurations (view / edit). Decided (F3): schedules reuse `.manage` |
| `recommendations.read` / `.decide` | inbox (view / approve, reject) |
| `control.manage` | autonomy policy, kill switch. **Today the web checks `security.manage` for the kill switch; the redesign switches to `control.manage` (E3).** |
| `logic.read` / `.manage` | module catalog / authoring and package upload |
| `connections.read` / `.manage` | connection status / setup and point map |

**State vocabulary.** Every data region implements the same five states, in this order of precedence: *access denied* (hidden or one-line notice), *loading* (`LoadingIndicator`, only on first load; refetches keep old data), *error* (`role="alert"`, Retry, keeps last good data marked stale), *empty* (explains why and offers the next action), *stale* (data older than its expected cadence: warning `Status` + age text such as "Updated 3 min ago"; never silently shown as current).

**Real-time (ADR-008).** The hub sends only a change area name; the client refetches. Existing areas: `executions`, `recommendations`, `configured-logic`, `assets`, `modules`, `connections`, `control`. Polling stays as safety net (10 s when disconnected, 60 s when connected). `LiveStatus` shows connection state. Proposed new areas (open question): `schedules` (or reuse `configured-logic`) and `authoring` (draft build state, G2/D6).

**Accessibility baseline.** Everything keyboard reachable; focus ring from tokens; status never by colour alone (`Status` has shape + word); live regions: `role="status"` for routine updates (polite), `role="alert"` for failures and kill switch changes; dialogs trap focus and return it to the trigger; tables have `scope` headers and a caption or `aria-label`; the whole row opens an item while the name stays a real button; targets at least 2.75 rem on touch; reduced-motion respected for spinners and flashing updates; timestamps are `<time datetime>` with absolute value in a tooltip.

**Time display.** Relative ("2 min ago") with absolute UTC and site-local on hover/focus. Ages in seconds below 2 min.

## Top questions for the owner

Each file ends with its own open questions; the cross-cutting ones:

1. Should schedules get their own change area and top-level route, or live only inside Logic > configuration?
2. Which role may use `control.manage`, and is it scoped per configuration (grant scope) or only per asset?
3. Is approve and execute one action or two (a human approves, the system executes after the gate)?
4. Is multi-provider authoring (D9) in the first authoring redesign, or only designed for?
5. Should point-map edits take effect immediately, or be versioned and approved like a connection revision?
