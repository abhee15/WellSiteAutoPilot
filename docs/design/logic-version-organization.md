# Logic and module version organization

Tracking: [#462](https://github.com/abhee15/WellSiteAutoPilot/issues/462). Follows the [presentation review](presentation-consistency-review.md).

## Decision

Use the existing master/detail structure rather than another nested tab hierarchy or a fully expanded version tree.

- **Modules:** one row per stable module ID. The row carries its version count and the exact version whose approval state is shown. Selecting it opens the module inspector, with a version selector that lists versions in SemVer order and includes their approval state. Older approved versions remain accessible when a newer version is unapproved. Selecting a version remounts version-specific controls so a pending approval form cannot carry into another package.
- **Configured logic:** one row per configuration ID. Keep revisions under that configuration, as the existing revision editor already does. Do not combine different target/schedule configurations because they share a display name or module. Carry the current target and module/version into row metadata so opening a narrow inspector does not remove all operational context. Show the number of drafts without replacing the active revision with a draft.
- **Schedules:** remain configuration-scoped. Grouping presentation does not reassign wells, activate revisions, change package versions or modify schedules.

## Filters, ordering and limits

Filter only authorized loaded records, then group the matching versions. Opening a family picks its highest matching SemVer version, not a newer filtered-out version. Counts distinguish loaded versions from module families; a partly matching family reports its matching-version count. The inspector still offers explicitly chosen sibling versions from the authorized loaded catalog. No extra unscoped query or implicit upgrade occurs.

The module list retains the existing name/installation sorting of the matched records. The version selector uses SemVer precedence, including numeric prereleases and build metadata. It does not assume lexical sorting (`1.10.0` follows `1.9.0`) or convert arbitrarily large numeric identifiers to unsafe JavaScript numbers.

## Why this fits

The user wants related versions together, while operators need to distinguish what is approved, what is running, and which wells are affected. A single family row reduces repeated module names; the existing inspector keeps exact package state visible without a table inside a table. Configuration revisions already have a legitimate common parent, while independent configurations do not.

This follows [Carbon table disclosure guidance](https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines), the existing shared-tab contract, and [SemVer precedence](https://semver.org/). The version selector is version selection within a record, not a new page-navigation tab.

## Validation

- Unit tests: same display name/different module IDs; filtering before grouping; numeric/release/prerelease/build ordering; non-mutating input and very large numeric identifiers.
- Interaction tests: one family row, exact-version selection, approval/detail identity and existing module/configuration behavior.
- Browser fixtures: two versions with different approval states, old-version selection, searching for the older version, tab style parity and no horizontal page overflow at 1440/900/420/320px.
- Existing full frontend interaction suite passes with 536 tests. Live package readiness is not asserted by these fixture checks.
