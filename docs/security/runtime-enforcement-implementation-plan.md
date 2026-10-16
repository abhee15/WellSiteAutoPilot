# Remaining enterprise RBAC runtime replacement

Implementation checkpoint derived from ADR-006 and #76/#77, after PRs #121–#124.
This is a work plan, not an authorization switch or a completion claim.

## Verified baseline

- Windows token proof and pure/SQL paired direct/multiple-group evaluation exist.
- User/group assignment authoring, custom roles, own-access protection and
  direct-recovery-administrator retirement protection exist.
- The frontend consumes server permission keys and isolates denied work areas.
  The server still derives those keys from legacy authority.
- The four `ScopedAccessQueries` resource families are prepared and tested,
  but published routes still use legacy `AssetScopeAuthorizer` methods.
- PR #125 merged as `7c9c0d7984b443b8b4e645cd3e835ea1a825b15a` after all
  seven jobs passed head `5deaaca188f962a0b1b86f4d3967caf70b32538d` in run
  `38947245574`. Its PostgreSQL matrix covers actual role-removal/user-deactivation
  lock waits for current role/grant writes.

## Next implementation sequence

1. Introduce one request-bound verified subject context: active persisted user,
   trusted enabled Windows-group proof and evaluation time. Reuse the provider
   resolver and proof types; never accept group proof from an HTTP DTO/header.
2. Complete grant bootstrap and explicit development reseeding/reset instructions.
   A protected recovery Admin needs a direct `DeploymentWide` assignment;
   resource-wide access requires its separate explicit `AllAssets` assignment.
   Do not infer an Asset wildcard from the Admin name or `security.manage`.
   Preserve the existing one-time bootstrap serialization and atomic audit.
3. Replace coarse permission policies and `/security/me` hints with the same
   paired-grant source. A coarse permission is only a prerequisite; do not turn
   it into universal resource access. Fail closed for absent/inactive identities,
   grants, roles, unsupported permissions and unusable membership proof.
4. Wire Assets, all disclosed Configured Logic revisions/input Assets, V1/V2
   execution snapshot scopes and Recommendation target Assets directly through
   composed SQL before ordering/pagination. Do not materialize an unbounded
   independent Asset list and then apply a separately unioned permission set.
5. Resolve mutation targets from persistence inside their database transaction.
   Hold qualifying user/grant/role authority rows through commit, plus target
   rows whose state could change the decision. Reuse SQL/domain semantics.
   A serializable snapshot alone does not fence revocation commit ordering when
   the authorized write targets another table. Conflicts fail closed without
   blind retries. Exact-grant Asset bindings remain immutable through current
   administration routes; future edits must participate in this same boundary.
6. Keep deployment-wide Logic catalog and security operations distinct from
   Asset scopes. Preserve the documented strict administrator rule for root
   creation/type-catalog operations until their explicit permission design is
   resolved; never treat one Well grant as universal catalog authority.
7. Remove legacy role/scope claims, wildcard authority, fixed-role access editing,
   migration-only preflight/materialize/rollback APIs and their UI together when
   the complete replacement passes. The old user-directory parser rejects
   custom role labels; replace its legacy authority assumptions as part of this
   removal. Keep identity listing separate from role assignment.
8. Run the full route/permission and direct-plus-multiple-group matrix, positive
   and denied cases, stale/malformed/inactive records, cross-scope configurations,
   paging, atomic audit failure and concurrent revocation. Validate frontend
   permission/scope loss and all seven jobs on the final reviewed commit and
   unchanged target/merge tree before switching the live authority.

## Working branch checkpoint — 2026-10-16

PR #126 merged as `3517bc812bca701bf057b1a023503c954a2b9cfb` after all seven
jobs passed exact head `67588522488302ad7f723c81f62a5f1562d6aa4b` in run
`38948432885`. The reviewed merge tree matched the feature tree.

`feature/unified-grant-runtime` adds an immutable authenticated subject, fresh
request permission queries and a paired-grant transaction fence. Coarse
permissions use the same eligible grants and scope validation as resource SQL;
explicit AllAssets authority remains discoverable with zero active Assets.
DeploymentWide never implies resource access. There is no permission cache.

The fence requires a caller-owned transaction and holds the selected grant,
user, role, permission, bindings and participating Assets until commit. It
rechecks only held assignments and proof freshness after acquiring locks.
Callers must still resolve and lock persisted business targets in that same
transaction, commit mutation/audit atomically, and translate commit conflicts.
This is not wired into live routes yet and does not activate mixed authority.

Validation includes a 20-case PostgreSQL direct/group resource/global matrix
that observes real revocation lock waits for user, grant, role, permission,
binding and Asset changes, then checks post-revocation refusals. These tests
must pass in CI before making any validation claim. Next implement bootstrap,
request-host binding and resource repository/route integration, then remove
obsolete editing/migration surfaces as listed above before runtime activation.

## Release limits

Genuine browser Negotiate, domain/nested groups, cross-domain trusts and token
renewal/revocation latency remain release gate #92. TestHeader and injected
proof fixtures do not establish those properties. Keep execution in Shadow.

Do not introduce a Legacy/Observe/Enforce state machine or a production
migration campaign for this unreleased baseline. Do not enable a partial mixed
legacy/grant runtime merely because an individual repository or UI test passes.
User observations and the deferred enum-generation decision remain recorded in
`docs/development/decision-followups.md`; they do not automatically change the
active enterprise RBAC priority.
