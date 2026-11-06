# Shared record and destination links

Implementation slice for #471, following detail-copy cleanup #478. No palette or Overview layout change.

## Decision

Use `ContextLink` for standalone destinations. Add `appearance="record"` for record names and timestamps
in tables, summaries and fact lists: it inherits surrounding type size, uses medium weight, preserves a
visible chevron and has no persistent underline. Keep native anchors, hrefs, accessible labels and
open-in-new-tab/browser navigation behavior. Do not replace a destination with a button.

Default destinations retain the existing control rhythm. Record links use a compact 24px minimum target
on fine pointers; both appearances use 44px on coarse pointers. Both share underline-on-hover and a
visible focus outline. Long labels wrap within the available column while the cue remains visible.
Visited internal SPA record links deliberately retain the same appearance: visiting a record is not a
completed workflow state. Existing status/selection components express those states separately.

Migrate Run details, Schedules, Logic/Asset run previews, decision/execution briefs, Asset names and
recommendations, Status connection names, import completion destinations and Overview destinations.
Remove obsolete `.text-link`, `.asset-logic-name` and Overview anchor appearance overrides. Place the
Run detail revision on its own metadata line so a wrapping name does not leave a dangling separator.

The owning-connection link inside Asset detail prose remains native and underlined. Section navigation
keeps its tab/navigation owner. Actions keep ActionButton/ActionLink. New raw view anchors require an
explicit semantic exception in the adoption guard; page styles cannot override shared link appearance.

## Research and review

[Carbon link guidelines](https://www.carbondesignsystem.com/building-blocks/core/components/link/guidelines)
separate standalone destinations from inline prose. [W3C G183](https://www.w3.org/WAI/WCAG22/Techniques/general/G183)
is a sufficient technique for color-only inline links, not a rule to remove underlines everywhere. The
current link/primary-text tokens have about 2.15:1 contrast, so color alone is inadequate for that
technique. This design instead keeps a non-color cue on standalone links and an underline in prose.
The blue/green/teal palette review remains queued in #479.

The rendered first pass exposed a specificity conflict: the compact attribute selector defeated the
coarse-pointer target rule. The shared media rule now explicitly covers both appearances. Review of
Run details also identified the wrapping revision separator; it is now secondary metadata.

## Validation

- Production build/typecheck, lint, 351 UI checks and 555 interaction tests. The import permission test
  now queries the actual link role instead of assuming the text node is the anchor.
- `audit-links.py` compares six populated scenes at four widths (48 before/after cases). After-fix
  assertions cover no persistent underline, visible destination cue, at least 4.5:1 text contrast to
  the actual background, fine/coarse target size, keyboard focus, long unbroken labels and page overflow.
  The audit uses local mock GETs only and does not submit actions. It uploads JSON in visual-review CI.
- Existing recommendation/control, authorization, responsive and run-history tests remain required.
- Broader #468 copy/type and #473 surface reviews remain open. #479/#480 new observations stay queued.
