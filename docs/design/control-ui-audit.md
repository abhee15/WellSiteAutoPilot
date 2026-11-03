# Audit of the control screens against the design system

Status: **findings and plan, for review.** Written after the owner's review on 2026-11-02: the recommendation approval, kill
switch, control actions, command execution and notification screens were built quickly and are not part of the design
system. This document records what is wrong (from the code, with evidence), what replaces it, and the rule that applies to
everything built from now on. It complements `control-screens.md` (the layouts to build) and `ui-conventions.md` (the rules).

## 1. What was audited

`KillSwitchPanel`, `ControlActionsPanel`, `ControlExecutionPanel`, `RecommendationDecisionReviewPanel`,
`RecommendationInboxControls`, `RecommendationsPage`, `AutonomyPolicyPanel`, `ConfiguredLogicSchedulePanel`,
`ExecutionDetailPanel`, `OperationsPage`, and the stylesheets they use. Evidence is from reading the code and measuring it
(class names against style rules, raw form controls, message markup). It is not from rendered screenshots: the generated
API client is produced in CI, so the app cannot be built in the working environment. Screenshots become part of every
pull request below (section 5), so the next review sees the rendered result, not only the code.

## 2. Findings

| # | Finding | Evidence |
| --- | --- | --- |
| A1 | **Panels with no stylesheet.** Elements use classes that no CSS rule defines, so they render with browser defaults and only what `.panel` and the global form rules give them. | `kill-switch`, `kill-switch-form`, `control-actions`, `control-actions-list`, `control-actions-resolve`, `autonomy-policy`, `autonomy-policy-form`, `autonomy-policy-limits`, `autonomy-policy-row`, `schedule-control`, `schedule-control-form`: 11 of the 17 classes in the whole app that have no rule. |
| A2 | **No shared form components.** Every panel builds its own label, input, helper and error from raw elements, and borrows classes from another feature to look right. | 22 raw `input`/`textarea`/`select` elements in these panels; the control panels reuse `recommendation-review-reason` and `recommendation-review-confirm` (defined for the approval dialog) for unrelated fields. |
| A3 | **No shared notice.** Errors, progress and confirmations are hand-written paragraphs, so they differ in look, placement and urgency. | `className="execution-detail-error"` used 14 times across the views for errors; plain `<p role="status">` for progress; a one-off `inbox-error` variant. No persistent versus transient distinction. |
| A4 | **Always-on panels crowd the working area.** The kill switch and the open control actions sit as two full panels above the summary cards and the list on the Recommendations page, whether or not anything needs attention. | `RecommendationsPage.tsx` renders both before `summary-grid` and the split layout. This breaks "quiet when nothing is wrong". |
| A5 | **Control actions are sentences, not data.** Command, value, time and wait are joined into one paragraph with separators, and the status and reason are concatenated into another. | `ControlActionsPanel.tsx`; the shared table (Status first, row opens details) is not used. A status with no message shows its raw code. |
| A6 | **Consequential actions expand inside list rows.** "Resolve" opens a form in place, pushing the list down, with its own radio and text inputs. | `control-actions-resolve`. There is no shared confirm pattern; browser dialogs are rightly banned, so a shared in-page one is needed. |
| A7 | **Statuses are inconsistent.** The kill switch uses `Status` with a tone; control actions print plain text; the policy editor prints nothing for state. | Compare `KillSwitchPanel` and `ControlActionsPanel`. |
| A8 | **Policy editor is free text.** Command and unit are typed, so a typo or wrong case silently makes a policy that never matches. | `AutonomyPolicyPanel.tsx` (text inputs for command and unit). Server now rejects undeclared commands, but the screen still invites them. |
| A9 | **Nothing stops it recurring.** The existing guards cover buttons, status pills, search and type, but not form controls, messages or unstyled classes. | `tests/consistency.test.mjs`. |

## 3. What replaces it

The foundation comes first, then each screen is rebuilt on it. Layouts are in `control-screens.md`.

| Step | Delivers |
| --- | --- |
| D1 | **Shared form and notice components.** `TextField`, `TextArea`, `NumberField`, `Checkbox`, `RadioGroup`, `SelectField` (label, helper, error and required handled once), `FormActions`, `Notice` (inline and banner; info, success, warning, error; correct `role`; persistent or dismissible), and an in-page `ConfirmPanel` for consequential steps. Tokens only; documented in `ui-conventions.md`. Guards: no raw form controls outside `components/`, and every error or progress message goes through `Notice`. |
| D2 | **Control status strip** (S4): kill switch, deployment switch and sink on one line in the page header area, abnormal state as the single thing shown. The two always-on panels leave the Recommendations page. |
| D3 | **Control activity** (S3, S5): the shared table and a details card in Operations, replacing `ControlActionsPanel` and its in-row forms. Open actions show as a count badge. |
| D4 | **Recommendation review and execution** rebuilt on D1: the approval, the reason and the confirmation as one consistent flow; the notifications as `Notice`. |
| D5 | **Autonomy policy and command catalog** (S2, S1): pickers from declared and catalogued commands, ceilings shown beside inputs. The schedule panel moves onto D1 in the same step. |
| D6 | **Evidence and guards.** Rendered screenshots of every screen attached to its pull request; the CSS-coverage guard below tightened to zero. |

Each step is a small pull request with green CI. The control framework (F6 onwards) continues in parallel; F8's screens are
the D2 to D5 screens, so they are built once, on the new foundation, not twice.

## 4. Guard added now

`tests/css-coverage.test.mjs` fails when a component uses a class that no style rule defines. It starts with the 17 classes
that already lack a rule and can only shrink: a new unstyled class fails CI, and removing the last use of a listed one (or
giving it a rule) forces its entry out of the list. Steps D2 to D5 empty the control entries.

## 5. Definition of done for any screen from now on

A screen is not ready for review until all of these are true. The pull request description answers each.

1. **Specified first.** Its layout, states (loading, empty, error, read-only) and copy are in a design document, and the build matches it.
2. **Shared parts only.** Buttons, fields, notices, tables, status, tabs, filters and confirmations are the shared components. No per-feature restyling, no borrowed class names, no raw form controls.
3. **Every class has a rule.** The CSS-coverage guard passes with nothing added to its baseline.
4. **Tokens, not values.** Spacing, type, colour and size come from `tokens.css`; no raw colours or pixel sizes outside hairlines.
5. **Words through `t()`.** No literal sentence in markup; every status and gate code has a plain-language message (the existing guard).
6. **Works at every size and by keyboard.** Phone, tablet and split-pane widths; focus order; announced errors; status never by colour alone.
7. **Seen rendered.** Screenshots of each state, in light and dark if both are supported, are attached and checked against `ui-conventions.md` before the pull request is merged.
8. **Guarded.** A structural test for the screen, and a new guard when a new rule is introduced.
9. **Only what applies.** Every control, panel and column is checked against the thing on screen (operating mode, permission, state). Anything that cannot apply to it is not shown, and where that leaves a gap one plain line explains why (`control-screens.md` section 1a).
10. **The whole page, before and after.** Before building, the page is rendered as it is today and looked at, to see what is missing, what does not belong and how the new part sits among its neighbours. After building, the same page is rendered again and the two are compared. Screenshots of both go in the pull request.

## 6. Questions for the owner

1. Is the order D1, then D2 and D3 (so the Recommendations page is calm first), then D4, D5 right, or is another screen more urgent?
2. Should the status strip live in the page header on every page, or only on the Recommendations, Operations and logic pages?
3. Is a screenshot review by you before each screen merges wanted, or are screenshots attached for later review enough?
