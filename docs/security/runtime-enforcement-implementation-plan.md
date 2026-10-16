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

The next checkpoint adds `GrantBootstrapRepository` and a scoped host
`RequestAccessContext`. Bootstrap persists separate direct DeploymentWide and
AllAssets grants with one audit and a cross-instance initialization lock. Any
prior grant/bootstrap audit prevents automatic regrant. Its disposable-database
tests cover simultaneous attempts, audit failure, invalid system role and a
genuinely empty Asset store. Initialization/reset assumptions are documented in
`docs/development/grant-runtime-initialization.md`. The context binds one
persisted identity to one trusted provider observation and excludes TestHeader
from Windows membership. These services are registered but not called by live
middleware yet; request/route integration remains the next implementation step.

The resource repositories now expose grant-authorized list/detail entry points
for all four families. They compose the paired SQL predicates before filtering,
ordering and limits. Configured Logic predicates and all revision/binding
hydration share one repeatable-read snapshot; an existing weaker transaction is
rejected. New deterministic PostgreSQL tests commit an out-of-scope revision
between predicate execution and hydration, verify no disclosure, and verify
that the following fresh read loses access. These entry points are not yet used
by published routes. Route/application integration and mutation target fences
remain required before activation.

Recommendation decisions now have a grant-authorized repository entry point.
It locks the persisted recommendation before resolving its target, retains
paired authority through commit, checks group freshness after business-state
locks, and commits the decision with its actor/correlation audit. Approval also
holds/checks the current configuration, execution and target snapshot provenance.
The new tests cover separate direct decision/group read scopes, expired or
duplicate decisions, malformed source provenance, injected audit failure and
actual concurrent grant-revocation waits. This entry point is not yet called
by the live API; current-head PostgreSQL validation is required.

Frontend integration must include resource-specific action capabilities, not
just coarse menu permissions: e.g. a reader on Well B with decision authority
only on Well A must not receive an enabled decision control for B. Calculate
those hints from paired grants on the server, generate the changed client and
test the denied/stale interaction. Backend transaction checks remain mandatory.

Verified foundation CI checkpoint: head
`2872da778555330abc4bf012809d49e00ee7c761`, run `38953414941`, passed the
PostgreSQL job including all 20 revocation cases, fresh-database bootstrap and
the existing security/migration suites. At that observation the Windows
installer job was still running; this is not a seven-job or current-head claim.

## Release limits

Protected-administrator classification is now available for request checks and
transaction fences. It requires a usable direct/verified-group DeploymentWide
grant of the protected system Admin role. Coarse `security.manage` hints and
request/transaction checks preserve that ADR-006 privilege-class restriction;
a custom role's known permission string does not imply protected administration.
Generic tuple evaluation remains separate from that additional product boundary.
New bootstrap-fixture tests cover direct/group positives and missing/stale proof
and custom-role negatives. Live administrator service/repository replacement
still needs to use this authority in place of legacy roles.

An explicit AllAssets transaction fence is also prepared for first/root Asset
creation and privileged catalog workflows. It works without existing Assets;
an empty target list is never accepted as its substitute. Tests remove only
the bootstrap AllAssets grant and verify that the remaining protected global
administrator cannot acquire a resource wildcard. Root/type-catalog integration
must retain the protected-administrator prerequisite **and** explicit AllAssets
`assets.manage` authority; ordinary child creation checks its persisted parent.

Asset creation preparation now extends through application and persistence:
`CreateAssetTypeAuthorizedAsync` / `CreateAssetAuthorizedAsync` build validated
records, and their repository writes hold the current type schema and
parent/grant authority in one serializable transaction with the creation audit.
The repository revalidates attributes against its locked schema. Duplicate
type keys become explicit conflicts; serialization/deadlock conflicts fail
closed without retry. Exact parent access does not automatically grant access
to the new child. `HasAllAssetPermissionAsync` supplies the corresponding
capability prerequisite without enumerating deployment Assets.

New tests in the isolated bootstrap database cover first type/root creation,
direct child-management plus group read scope, cross-scope/type/root denial,
noninheritance, changed-schema validation, atomic audit failure, duplicate keys
and an actual grant-revocation wait during child creation. These methods remain
unwired from live routes and require current-head CI. Next complete Configured
Logic lifecycle and manual Shadow request/replay transaction fences, global
catalog/security write authority, then the unified request/API/frontend switch.

Genuine browser Negotiate, domain/nested groups, cross-domain trusts and token
renewal/revocation latency remain release gate #92. TestHeader and injected
proof fixtures do not establish those properties. Keep execution in Shadow.

Configuration draft persistence now has grant-authorized create/revision entry
points. Revision authoring locks the existing definition, revisions and both
target/input binding sets, then retains paired read/manage authority for every
persisted and incoming Asset through the write/audit commit. Incoming bindings
cannot replace a foreign configuration's existing scope. Initial creation
requires a single explicit Shadow draft; empty targets never imply a wildcard.
Application manifest/binding validation still precedes these persistence calls;
these entry points do not replace it and are not wired to live routes yet.
Validation/activation dependency fencing and application integration remain next.

The added PostgreSQL cases cover direct and verified-group manage authority
paired with separate direct read authority, cross-scope revisions/data inputs,
foreign-configuration takeover attempts, stale revision numbers, actor mismatch,
missing group proof, an injected post-save rollback, and an observed grant
revocation lock wait followed by fresh denial. Local compilation, Npgsql query
translation and 14 convention checks pass; actual PostgreSQL execution must be
verified on the new head in CI before claiming this checkpoint validated.

User-approved next Phase 1 priority after completing #127 remains #92: start
with a separate manually triggered Windows browser diagnostic PR, then grow
real local user/group coverage and relevant-change regression gating. Keep
domain-connected AD/Kerberos validation distinct from local Windows coverage.

Do not introduce a Legacy/Observe/Enforce state machine or a production
migration campaign for this unreleased baseline. Do not enable a partial mixed
legacy/grant runtime merely because an individual repository or UI test passes.
User observations and the deferred enum-generation decision remain recorded in
`docs/development/decision-followups.md`; they do not automatically change the
active enterprise RBAC priority.
