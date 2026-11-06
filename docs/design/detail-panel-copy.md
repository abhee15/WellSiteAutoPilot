# Detail-panel copy cleanup

Follow-up to #468, after the blocking command-panel scroll fix #477.

## Decision

Logic and Asset details should present identity, current values, recent activity and actions without
repeating routine display rules. The timestamp formatter already includes its time zone. Remove the
local-time/runtime disclaimer from both detail surfaces, the successful operational-read footer from
Logic Overview, and normal-state schedule agreement/revision-implementation paragraphs. Remove their
unused localization keys as well.

A real live/saved schedule difference still displays a compact saved-frequency reference next to the
current state and existing controls. Keep shared-view scope, because a schedule change affects all
members; keep revision-change notices, refresh errors/retry, adapter limits, field help and action
acknowledgements. Do not hide useful exceptions merely to shorten a panel.

This follows [Atlassian's content guidance](https://atlassian.design/foundations/content/) on concise,
consistent interface language and [W3C's cognitive accessibility guidance](https://www.w3.org/WAI/WCAG2/supplemental/patterns/o5p03-manageable-quantity/)
on reducing unnecessary content while retaining important cues and instructions. Applying those
principles here is a product decision based on the user's reported detail-panel task.

## Validation

- Production build/typecheck and lint passed; 350 UI checks and 555 interaction tests passed.
- Real Chromium review at 1440, 900, 420 and 320px: normal, changed, read-only schedule and failed activity
  reads (16 cases). Checked actual rendered copy, retained difference/error states, absent management
  action when read-only, horizontal overflow and no writes. The activity error remains visible rather
  than an unqualified freshness claim.
- Shared consistency guard rejects reintroduction of the retired boilerplate keys throughout the app.
- No style/layout, scheduling, permission or command behavior changes. The existing visual workflow
  continues to review primary pages and responsive layouts.

## Remaining review

This slice addresses the explicitly rejected boilerplate, not the complete #468 audit. Review the
remaining Configuration/Activity and run-detail prose in task context; retain information needed to
review immutable revisions or make a consequential decision. Link presentation is tracked in #471,
flat/selected surfaces in #473 and command meaning/provenance in #474.
