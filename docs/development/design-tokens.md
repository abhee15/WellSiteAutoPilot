# Frontend design tokens (V1)

The frontend uses **CSS custom properties**, not a new UI framework. Load
`src/tokens.css` before `src/styles.css`.

## Agreed design reference and implementation gap

The user identified this [Claude design reference](https://claude.ai/share/39615aa3-bd6c-4812-976c-edb0ec90a284)
on 2026-10-16 as the agreed design. The shared conversation was inspected in
the browser, including the later refinements through prototype version 10.
Attachments and the running prototype are hidden in this shared snapshot;
exact token values and rendered visual parity still need the prototype source.

The verified visual and layout direction is:

- A modern engineering workstation: neutral/slate canvas, white/brighter
  surfaces, subtle borders, clear typography and one restrained blue accent.
  Reserve red/amber/green mainly for operational semantics. Show modes through
  labels, icons, borders and subtle accents instead of saturated surfaces.
- Organize disclosure into Operate, Understand and Diagnose/Configure. Favor
  role/task defaults and engineering concepts; expose provider-specific
  mappings and infrastructure diagnostics on drill-down.
- Primary navigation: Operations, Assets, Logic and Recommendations, subject
  to permission. Operations centers on Active logic with one contextual rail:
  My attention until a row is selected, then its inspector. Avoid KPI/card walls.
- Logic detail: Overview, Activity, Configuration and Engineering. Preserve
  Asset, Logic Module, mode, execution, attention and control ownership context.
  Show meaningful permitted actions without rows of disabled controls.
- Keep the signed-in user at the bottom left. Click/keyboard activation expands
  permitted account/support options **inline within the sidebar**, with quieter
  type and indentation. Do not use a floating account menu or invent an editable
  Profile page for enterprise-owned identity. Audit, System Health and
  Administration belong in this secondary area when permitted.
- Keep the essential desktop task within a typical 1440px workspace using
  tabs, split panes, inspectors, sticky context/actions and focused internal
  scrolling. Configure Logic remains a stepwise workflow. Adapt for smaller
  panels; do not force a fixed desktop viewport or hide essential actions.
- The Activity panel contains attention plus a contextual live feed and scrolls
  independently. Routine sensor updates stay quiet; meaningful state changes
  update visibly and actions requiring a person raise attention. Preserve
  selection, sort, filters and scroll during live updates. Show quality,
  freshness, timestamps and connection state wherever they affect a decision.

The current dark CSS is an implementation gap, **not the approved palette**.
Its regression checks preserve existing declarations; they do not establish
conformance to this design. Align styling in a focused change using the actual
prototype/source for exact values, without blocking backend authorization.

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
