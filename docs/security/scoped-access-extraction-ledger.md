# PR #79 extraction ledger

Original reference: `feature/scoped-access-entitlements` at
`38fbf5e9cfdb3db31235555421ce8515801d3009`. Preserve this branch and PR #79
unmerged until extraction has been verified. Extract onto current `develop`;
never restore its complete old tree or overwrite newer work to reduce a diff.

## Cohesive implementation slices

| Functionality from #79 | Extraction |
| --- | --- |
| Scoped grant model and PostgreSQL foundation | #102 |
| Pure, fail-closed grant evaluator and isolation tests | #103 |
| Audited administrator grant APIs and assigned-role protection | #104 |
| Reviewed grant administration UI, bounded Asset directory, role revision fence | #105 |
| Reviewed API authorization metadata inventory | #106 |
| Diagnostic SQL grant projection and pure/SQL parity | #107 |
| Pure legacy migration planner and parity evaluator | #108 |
| Audited effective-access diagnostic API and UI | #109 |
| Bounded, audited single-user and deployment preflight APIs | #110 |
| Read-only migration preflight UI and interactions | #111 |
| Atomic legacy grant materialization and provenance-checked rollback APIs | #112 |
| Explicitly confirmed migration/rollback UI and stale-data invalidation | #113 |
| Audited, paged proposed Asset access API and UI | #114 |
| Legacy malformed-scope guards and retained cutover acceptance checklist | #115 |

PRs #102–#111 are integrated in baseline `94458429`. PR #112 merged as
`46080ebf09b7d150b96c981046a3d878abef28cc`, after all seven jobs passed for
`df2dcd2e72c2d30ea0477462994243c2fc3b7547` in run `38834417336`.
PR #113 merged as `7fc9480db498545311d82d8c7a437f648844dc2b`, after all
seven jobs passed for `a2dabc78d2b15fb8cc5586339f4e7b6c4953bf89` in run
`38835101475`. PRs #114 and #115 require their own final-head validation and
review; inclusion in this table is not a claim that an open PR is merged.

## Intentional differences from the original branch

- Retain the extracted, separate evaluator, parity, persistence and interaction
  test files. Do not replace them with the original monolithic test program or
  remove the pure evaluator suite when adding legacy boundary tests.
- Retain generated API response types, shared nonempty UUID validation, semantic
  design tokens, extracted shell components, authentication development proxy,
  and Windows Negotiate smoke checks already on `develop`.
- Preserve role revisions on grant creation, SQL rejection of empty Asset IDs,
  complete bounded snapshot validation, and durable diagnostic audits. The
  original branch lacks some of these subsequent fixes.
- Keep transport contracts separate from application models. Do not move all
  new DTOs back into the original combined grant-contract file.
- Keep the existing serializable audit and grant-write transactions, rollback
  payload verification, retained-provenance capacity limit and real database
  failure/concurrency tests. A source-tree diff that removes these is a
  regression, not remaining feature work.

## Runtime authorization is still legacy

Materialized grants, effective-access comparisons, SQL previews, and preflight
results are diagnostic records. They do not replace Windows identity, legacy
roles/claims or live Asset scopes. Rejecting malformed legacy wildcard claims
and invalid requested Asset IDs does not grant any new access; valid explicit
scopes and the normal sole Admin wildcard retain their existing semantics.

The original #79 does not contain a complete secure runtime cutover. Remaining
work under #76 includes permission-aware resource resolution across every API,
SQL-filtered collection parity, transactional revocation fences, inactive-user
reactivation, deployments beyond diagnostic bounds, and a durable audited
Legacy/Observe/Enforce transition with rollback. Windows group proof remains
#77. See [the cutover checklist](scoped-authorization-cutover-checklist.md).
