# Frontend design tokens (V1)

The frontend uses **CSS custom properties**, not a new UI framework. Load
`src/tokens.css` before `src/styles.css`. Preserve our dark, responsive
industrial application appearance as the baseline.

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

## Scope and verification

This PR changes **only CSS values via var() substitution**, token definitions,
one CSS import, documentation and regression tests. No product UI behavior,
authorization, JS business logic, backend, or media-query breakpoints change.
The migration parity test verifies unchanged CSS semantics; native screenshots
and keyboard/contrast tests remain desirable for later UI-specific PRs.
