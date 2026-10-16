# Frontend design tokens (V1)

The frontend uses **CSS custom properties**, not a new UI framework. Load
`src/tokens.css` before `src/styles.css`.

## Agreed design reference and implementation gap

The user identified this [Claude design reference](https://claude.ai/share/39615aa3-bd6c-4812-976c-edb0ec90a284)
on 2026-10-16 as the agreed design. The earlier project discussion records a
**light neutral canvas, white surfaces and restrained blue accents**, with
red/amber/green used mainly for operational meaning. The Logic Instance detail
direction uses Overview / Activity / Configuration / Engineering sections.

The current dark CSS is an implementation state, **not the approved palette**.
Do not infer design approval from existing styles or their regression tests.
At this review, the shared conversation content was access-denied from the
available environment; exact colors, spacing and prototype details have not
been inspected. Retrieve the prototype/source before claiming visual parity
or choosing exact replacement values. Track that alignment separately from
backend authorization; do not let the current dark styling become a design
requirement by repetition.

## The two token layers

- `--palette-*` are private **primitive color values**. Only `tokens.css`
  defines raw hex colors. Do not import palette primitives directly into UI
  components or use fixed colors in new component CSS.
- `--color-*` are **semantic color aliases** consumed by UI controls:
  `--color-surface-panel`, `--color-text-primary`, `--color-border-control`,
  `--color-focus-ring`, and dedicated success/warning/error/info states.
  Some existing dark palettes look similar but differ deliberately to preserve
  visual parity. Consolidate only in a separate, reviewed design change.
- `--space-*`, `--radius-*`, `--font-family-ui`, `--font-size-*`,
  `--border-width-thin`, and `--shadow-popover` provide a baseline for
  component spacing, shapes, typography and depth. Add tokens for new
  *repeated* design values, not every unique responsive dimension.

The semantic status names express intent. Never use color alone to communicate
safety or operating state; keep readable labels and accessible focus/contrast.

## Development and review rule

1. Search for an existing **semantic token** first. For a genuinely new
   semantic role, add one documented alias in `tokens.css` (and a primitive
   palette token only if required). Avoid component-local ad hoc hex values.
2. Keep layout responsive and accessibility behaviors intact. Existing media
   breakpoints remain literal because CSS custom properties cannot be evaluated
   in ordinary `@media` conditions.
3. `npm run test:ui` executes `tests/style-tokens.test.mjs`; it rejects raw
   colors in frontend CSS and unresolved/malformed CSS variable definitions.
   During this initial mechanical migration it also verifies the resulting
   stylesheet expands to **exactly the same original declarations**.
4. When later UI work intentionally changes styles, update the parity test
   as part of the reviewed design change, or replace the migration-only parity
   assertion with durable visual regression coverage. Never silently delete
   guard tests to bypass a failure.
5. Do not add dark/light switch controls as part of tokenization. Themes and
   high-contrast variants can evolve later from the semantic layer, with
   accessibility and industrial operator review.

## Feature design review

Follow the linked agreed design and verified project decisions for palette,
application shell and navigation hierarchy. Administration stays in the
privileged user-menu area;
do not promote Audit or System Health into primary operator navigation.
Use the existing semantic tokens and shared controls before adding new styles.

Improve a design when a concrete workflow exposes a gap: record the problem,
keep the adjustment local, and review the affected normal, loading, empty,
error and permission-denied states. Check keyboard access and a narrow panel
viewport as well as desktop. Retain explicit operating-state labels, localized
text and locale/timezone formatting. Align the current implementation with
the agreed prototype in a focused
visual change; avoid inventing a replacement theme or rearranging the shell
as an incidental feature change.

Token checks establish color/declaration consistency, not visual proof of a
new screen. Review rendered UI for changes to layout or interaction.

## Initial tokenization scope and verification

The initial tokenization changed **only CSS values via var() substitution**, token definitions,
one CSS import, documentation and regression tests. No product UI behavior,
authorization, JS business logic, backend, or media-query breakpoints changed
in that tokenization. Its dark CSS parity checks describe implementation
preservation, not conformance to the agreed light design.
The migration parity test verifies unchanged CSS semantics; native screenshots
and keyboard/contrast tests remain desirable for later UI-specific PRs.
