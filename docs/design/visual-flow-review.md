# Visual hierarchy and task-flow review

Review date: 2026-11-05. Baseline: develop 603b5fb (#465).

## Review method and decisions

Pass 1 captures all eight navigation pages plus Modules, Schedules, and asset/logic/run/connection/recommendation details at desktop and phone widths. Pass 2 follows task paths (asset → logic → schedule → run → proposed change → decision), including read-only roles, empty/failed/loading content, selected records and keyboard focus. Pass 3 repeats rendered checks after the changes and measures control borders, focus, touch geometry and overflow. Preview data is representative, not evidence of live ForeSite behavior.

The light engineering-workstation palette is appropriate. More decoration would compete with operational states. Improve hierarchy, interaction cues and retained context before adding more colors or icons. Meaningful icons accompany words; actions retain native link/button semantics. Reserve amber/red for attention/problems, blue for navigation/selection, green for confirmed successful states. A completed calculation does not mean a proposed command was approved or executed.

## Page decisions

| Page | User question and main path | Findings / decision |
| --- | --- | --- |
| Overview | What needs me, on which well, and where do I go? | Keep attention before metrics/history. Put proposed command/value on its own line. Add navigation cues to linked metrics. Put first-time setup destinations inside attention; remove the below-grid idle-logic nudge, whose latest-loaded-run sample cannot establish that logic has never run. |
| Runs | What happened to which asset, with which logic, and what needs review? | Keep status/asset first. Compact rows must retain logic/version and outcome when columns disappear. A successful run and an awaiting recommendation remain distinct. |
| Recommendations | Which proposed change am I reviewing, and what does approval do? | Make proposed numeric value a primary datum; retain expiry beside it in narrow views. Give filter cards a filter/selected cue, and run choices a persistent selection check. Keep approval versus execution notice and all safety gates. When deployment-wide control is off, suppress the contradictory released-switch explanation that commands can flow. The kill-switch action stays available as a secondary action. On narrow screens, hide summary filters while a decision is open; keep Back and the control state available so the selected task is reached without scrolling past unrelated counters. |
| Assets | Which source/view and wells am I working with? | Keep hierarchy/type icons, owning-source identity and read-only mapping behavior. Assign logic stays conditional on a selected saved view; no generic always-visible Assign to view action. Move the existing per-asset Put logic action beside the selected asset identity, ahead of technical details and run history, with its existing availability conditions. More icons in every row would add little meaning. |
| Logic / Modules | What is configured where, and which version am I using? | Retain current target and exact module/version in configuration rows; keep family grouping with explicit per-version approval. Do not merge different configurations into one name-based group. Existing revision controls stay inside details. |
| Schedules | What runs when, on which target, and how can I change it? | Keep read-only overview linked to its configuration, calendar units and timezone. The introduction links the user to schedule controls without claiming every change requires a revision. Existing wide table needs horizontal navigation at small widths; review representative non-empty schedule fixtures rather than an empty table. |
| Connections | Which integration needs setup/testing/import? | Name + adapter + health provide useful hierarchy. Preserve saved/dirty/locked distinctions and ordered save/test/discover/import actions. Legacy form/notice ownership remains D1/#418. |
| Status | Which service/source needs investigation? | Keep status icon + text. Add a restrained semantic surface/edge on abnormal health rows so problems are visible among healthy rows without recoloring the whole page or changing order on every poll. |
| Access / Activity | Who has access, through what assignment, and what changed? | Keep account identity, assignment counts, status and details; activity remains chronological. Repair preview user-page fixtures before treating preview failures as product defects. No extra decorative charts or exposure of unavailable identities. |

## Borders and accessibility

Default control borders already use --color-border-control; the preceding slice corrected recommendation selectors that used passive panel borders. Verify computed styles, not tokens alone. Required visual boundaries/selection/focus indicators meet 3:1 against adjacent surfaces; normal text meets 4.5:1. A passive divider is not an interactive control and should stay subtle. Error meaning is provided with text, not a pale border alone. Keep the separate focus ring and persistent selected state. Disabled controls are not treated as active contrast failures.

Summary-card visuals gain one CSS owner; page CSS may position cards but may not override their typography, colors, padding or borders. Compact Runs context is tested in a rendered browser because DOM tests cannot prove responsive visibility. Existing token/ownership tests remain mandatory.

## Evidence and limits

Record final test counts and responsive results in the PR. Fix stale preview endpoints (recommendation detail/run filtering, paged users, kill-switch shape, and schedule/run relationships) so review covers real rendered states. Review includes populated and empty pages, but does not certify every adapter setting, every long enterprise directory, or complete WCAG conformance. Shared legacy form/notice migration remains D1/#418; do not create another competing form kit.

## Primary guidance

- Carbon status indicators: https://www.carbondesignsystem.com/building-blocks/core/patterns/status-indicators
- Carbon data tables: https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines
- Atlassian border roles: https://atlassian.design/foundations/border
- W3C non-text contrast: https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html
