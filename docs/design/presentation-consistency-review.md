# Presentation and consistency review

Reviewed against `develop` at `d1b46d77`, 5 November 2026. Tracking: [#460](https://github.com/abhee15/WellSiteAutoPilot/issues/460).

## Findings and decisions

| Priority | Finding and evidence | Decision |
| --- | --- | --- |
| P0 | Assets `Assign logic` uses `.action-link`, while its neighboring controls use `ActionButton`. Rendered height was 28px versus 36px, with different padding and a chevron. | Correct in this slice: native `ActionLink` shares the existing action foundation with `ActionButton`. Migrate the equivalent asset assignment and Back to asset navigation. Preserve conditional view visibility, permissions, hashes and native link behavior. |
| P0 | Action controls have several style owners: shared component CSS, legacy global classes, and `.asset-view-bar .secondary-button`. The latter two override touch sizing. | Remove the toolbar action override and make the shared coarse-pointer action rule survive legacy variant CSS. Verify equal geometry and 44px touch targets in the browser. |
| P0 | At 320px the Assets grid's implicit column expands to 257px although its available width is 220px. The view select also had a fixed minimum. | Bound the shared outer grid column with `minmax(0, 1fr)` and allow the view field to shrink. Do not hide overflow or remove controls. |
| P1 | New assignment shows `Configure logic` in both the workspace frame and the embedded editor. | The workspace owns the heading. Preserve headings for standalone editors and distinct revision-edit contexts. |
| P1 | The Modules table treats each version as a separate top-level record. A single module with many versions dominates the list. | Group by stable module ID, never display name. Show one module row with version count and a clearly identified version; inspect versions together. Preserve approval/package identity per version. Filtering must reveal matching versions rather than silently substituting another one. |
| P1 | Configurations and module packages have different identities. Two configurations can use the same module but target different wells or schedules. Configuration revisions are already nested under the configuration in `ConfiguredLogicWorkspace`. | Keep one configuration per row and its revisions together. Do not merge configurations by name or module. Improve visible target, active revision and draft context; retain the reviewed revision and activation rules. |
| P1 | When a configuration is selected, `run-col-secondary` hides its target/revision/last-run columns to fit the list pane. The remaining name alone loses operational context. | Carry the target/view and module/version into compact row metadata, instead of relying on columns that disappear. The selected detail should lead with current behavior, then drafts, then history. |
| P1 | Assignment setup repeats a field heading, explanatory paragraph and label for simple controls; view identity appears in multiple sections. The screenshot is much taller than its actual decisions warrant. | Next layout pass: compact module/name section; one target summary; schedule; concise review; advanced settings and source provenance behind one disclosure level. Keep required settings and validation errors visible. Avoid turning independent sections into a step wizard. |
| P1 | New assignment uses a repeat number/unit; legacy `ConfiguredLogicSchedulePanel` frequency editing still asks for seconds. | Reuse the schedule field pattern when the schedule editing lifecycle is revised. Preserve server constraints and calendar anchors; do not disguise calendar months as fixed seconds or silently change live schedules. |
| P2 | Notices use `status-error`, `execution-detail-error` and bare alert markup in different screens. Form captions also have page-specific CSS selectors. | Follow the existing shared form/notice ownership in `control-ui-audit.md` and #418. Build one field/notice contract, then migrate consumers; do not create a second kit in this review. |

## Tab review

The styling migration exists: `PageTabNav` and `SectionTabs` both use `TabStrip` and `tabStrip.css`.
Page navigation remains native links with `aria-current`; detail panels retain tab/tablist/tabpanel semantics and keyboard activation.
Those semantics should differ; their visual foundation should not.

Browser comparisons of the selected Logic page section and selected configuration detail tab check font size/weight, text color, underline color and padding. The shared interaction suite checks native navigation, roving focus and activation. Keep these owners; do not introduce another tab component for version grouping.

## Link presentation contract

| Context | Presentation | Behavior |
| --- | --- | --- |
| Workflow entry or navigation beside action buttons | `ActionLink`, same action geometry and variant as its neighbors | Native anchor with a real URL; no button role or JavaScript-only navigation |
| Record drill-down in a row, card or summary | One consistent standalone/contextual link pattern; descriptive destination, optional directional icon | Native anchor; standard focus, hover and visited behavior; no bespoke per-page pills |
| Link within prose | Underlined inline link, same text size as its sentence | Native anchor; no icon or button frame |
| Same-page record selection, disclosure, filtering, save or approval | Existing button/tab/disclosure components | Retain the operation's actual semantics; do not convert to links for appearance |

The compact contextual links in Overview, Runs, recommendations and connection rows require an app-wide migration under this contract. They are not automatically interchangeable with toolbar actions. Inventory all consumers before changing the global `.action-link` class, including the user menu and control action deep links.

## Logic organization: implementation sequence

1. Fix the shared action/link mismatch, duplicate title and demonstrated narrow-screen defects in a focused PR.
2. Group module versions by module ID. Keep approval state and actions at version level. Use the existing master/detail pattern; keep matching search/filter results explicit, and preserve selected versions during refresh.
3. Improve configuration rows and detail hierarchy around target, active revision, drafts, schedule and latest run. Keep each independently scheduled configuration distinct.
4. Migrate contextual and inline links to their shared owners across the app; check keyboard focus, touch spacing, destination wording and visual emphasis in context.
5. Consolidate forms/notices with their existing owner, then reduce assignment form repetition using those primitives.

Each slice includes a rendered before/after review, not just a passing CSS coverage test. Source guards must check component ownership, while browser checks catch specificity and sizing regressions that tokens alone cannot prevent.

## Review coverage and limits

- Source review: Assets and its detail/history actions; Logic catalog/configuration lists; assignment and revision editors; schedule controls; shared tabs, actions, forms and notices; contextual links in Overview, Runs, recommendations and connection rows.
- Browser fixtures: Assets view selection and assignment-to-save path at 1440, 900, 420 and 320px; coarse-pointer action sizing; selected page/detail tab visual parity; native link destination and visible focus. Fixtures use demo data, not a live ForeSite server.
- This review does not assert full visual parity for every app screen, and does not change package approval, authorization, scheduler execution or database state.

## Guidance used

- [Carbon links](https://www.carbondesignsystem.com/building-blocks/core/components/link/guidelines): distinguish standalone and inline navigation; avoid a page full of competing links.
- [Carbon tabs](https://www.carbondesignsystem.com/building-blocks/core/components/tabs/guidelines): group related content; use filters for filtering and progress indicators for linear work.
- [Carbon forms](https://www.carbondesignsystem.com/building-blocks/core/patterns/forms): logical grouping, predictable sequence and progressive disclosure.
- [Carbon data tables](https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines): use disclosure for related secondary data without expanding everything by default.
- [WAI link pattern](https://www.w3.org/WAI/ARIA/apg/patterns/link/): preserve native browser link functionality.
- [NN/g progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/): prioritize common decisions and expose advanced options on request.

These inform the app's existing component system; they do not require adopting another component library or copying another product's exact button taxonomy.
