# Overview hierarchy: first typography slice

Tracking: #468, after #472. The broader page/section review remains open; link presentation (#471), flat selection/elevation (#473), command provenance (#474) and concise detail copy are separate queued slices.

## Findings and design

Overview used 16px panel titles, 20px figures and 12px attention metadata. Asset names inherited bold while headings were medium, making individual rows stronger than section boundaries. Latest runs showed technical module IDs even when the readable configured-logic name was available. Full-width attention, metrics and run sections created a long vertical scan.

The shared role scale now gives primary panel headings a section-heading token (18px medium), summary figures a metric token (28px bold, tabular numbers), summary labels 14px medium and supporting data 14px regular. Nested panel h3 headings remain 16px; compact recommendation filters remain 16px figures. All weights use the shipped Roboto 400/500/700 faces. These are semantic roles, not page-specific font overrides.

Overview keeps attention first in DOM and visual order. At wide content widths, a two-column top section places the KPI group beside attention. It stacks at narrower content widths; small metric groups become label/value rows without squeezing long labels. Latest runs follows, naming the configured logic when available and retaining the module ID fallback. Relative times expose their exact timestamp accessibly. No new data requests, counts, trends, status claims or permissions are introduced.

## Iteration and boundaries

Compare the existing populated page, a type-only iteration and the final layout at 1440/1024/420/320px. The first layout render exposed a global summary-grid declaration winning the grid rule; the corrected page layout uses an explicit wrapper selector. Shared component appearance still belongs to its owner. Default cards remain flat; selection/link styling is not changed in this slice.

Validate computed role sizes and weights against tokens, loaded font faces, horizontal overflow and all primary pages. Include clear, unavailable and restricted Overview states. Existing role, attention ordering/count scope and destination tests must continue to pass. The full typography/copy audit proceeds in subsequent reviewed slices; this PR does not close #468.

Local evidence: 349 UI tests, 555 interaction tests, production build/typecheck and lint passed. The reusable browser audit passed 88 before/after page/state/width cases; the existing run-history geometry check passed eight cases. Four-width baseline/type-only/layout comparisons were inspected. These are representative mock-data checks; they do not verify live provider behavior.

CI exposed the recurring Windows DevTools-startup timeout before layout assertions. The fixture now uses a fresh temporary browser profile outside the watched source tree, ignores generated artifacts in the Vite watcher, waits for the actual HTTP page target within a bounded 45-second window and uploads startup diagnostics. An intentional browser early-exit check fails promptly with diagnostics; all eight real geometry cases pass locally. Layout assertions are unchanged.

Remaining data-presentation finding: proposal summaries can expose quantity keys (for example PumpSpeed) alongside a readable command. Review that shared formatter and the remaining detail/metadata roles in the next slice rather than applying a special Overview-only text rule.

Primary guidance:
- https://atlassian.design/foundations/typography/applying-typography
- https://atlassian.design/foundations/typography
- https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines
