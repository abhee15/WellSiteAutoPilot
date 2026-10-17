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

Authorized application create/revision methods now reuse the existing bounded
manifest, parameter, binding and schedule validation. Revision preparation uses
the grant-filtered full configuration read; the persistence transaction still
rechecks the persisted scope and revision number. PostgreSQL coverage also
exercises both application entry points and malformed parameter refusal. Live
route integration remains deferred until the remaining lifecycle is complete.

Draft persistence checkpoint `15459c2a494a85299ca01def91c5e9173a781969`
passed PostgreSQL integration in run `38982639874`, including the new rollback
and observed revocation wait. At this observation six jobs passed and the
Windows installer job remained in progress. Application-method changes require
their own new-head validation; earlier-head CI does not validate them.

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

All seven jobs passed application-authoring head
`540797a9d1c4c9a7c50ef5b64a806f2419618158` in run `38982870375`.
The next checkpoint adds grant-authorized validation/activation application and
repository entry points. They compare the complete reviewed revision with the
locked persisted snapshot, retain complete configuration read/manage scope,
and retain/recheck the enabled installed module/manifest and required Asset
types through the atomic status/active-pointer/audit commit. Proof freshness is
rechecked after dependency locks. Activation requires the reviewed active token;
stale snapshots/tokens and dependency/serialization conflicts fail closed.
The existing legacy entry points remain until the complete route switch.

New lifecycle PostgreSQL cases cover separate read/manage grants, missing group
proof, out-of-scope inputs on an older revision, changed reviewed metadata,
module/type retirement between review and persistence, rollback after saved
status/audit SQL, missing/stale activation tokens, superseding active revisions,
duplicate activation and an observed module-retirement lock wait. After that
retirement a fresh draft cannot be validated. These cases require new-head CI;
the seven-job result above applies only to the prior checkpoint. Manual Shadow
request/replay and deployment-wide catalog/security mutation fences remain
before the unified middleware/API/frontend cutover and legacy removal.

Manual configured Shadow requests now have a grant-authorized preparation/write
path. It derives the request ID from the authenticated user, locks the request
and configuration dispatch keys, resolves the complete configuration from
persistence, and retains configuration-read / execution-request authority and
active revision/module/type dependencies through execution/outbox/audit commit.
It never accepts an execution snapshot supplied by an HTTP request. Existing
request receipts recheck current configuration-read and persisted execution
scope/request authority; V2 payload identity and complete scope must match.
Replay returns only the six-field receipt, including for completed requests;
status, payload and output stay behind the separate execution-read endpoint.
An authorized historical receipt can be replayed after module retirement, but
a new request cannot dispatch against that retired module. Concurrent database
conflicts fail closed without blind retries; a fresh explicit replay can obtain
the existing receipt. No live route has switched yet.

Manual-request PostgreSQL tests cover request-only callers, direct/group grant
checks, actor-bound request identity, duplicate replay, overlap/key conflicts,
malformed persisted scopes/snapshots, module retirement, post-save rollback of
execution/outbox/audit, and a real request-grant revocation wait before commit
followed by fresh request/replay denial. These require CI on the new head.

Lifecycle checkpoint `b5a5b16ec5406300f93c91e14ca7d81a59b1fff4` passed
PostgreSQL integration and five other jobs in run `38984995900`; the Windows
installer remained in progress at that observation. The manual-request changes
pass local full-solution compilation with zero warnings/errors, identity/pure
grant checks, Npgsql translation, 14 convention tests and convention validation.
None of those local checks substitutes for new-head PostgreSQL execution.

Manual-request head `3fab500fa6fc507e8ab60b779eed2e9b787da645` passed
PostgreSQL integration in run `38985438569`, including the new request/replay,
receipt integrity, rollback and observed grant-revocation cases. Five other jobs
also passed; the Windows installer remained in progress at this observation.

The next prepared boundary is module catalog registration. Its authorized
application path reuses manifest/checksum/trust validation; the repository
retains DeploymentWide logic.manage authority through module/audit commit and
rechecks it for idempotent registrations too. ExactAssets/AllAssets never
satisfy the global operation. Installed version content/trust remains immutable;
concurrent writes/revocation surface conflicts without blind retry. Existing
legacy/CLI methods remain until the complete route cutover. PostgreSQL tests
cover direct/group global grants, resource-vs-global isolation, missing proof,
idempotency after permission loss, immutable content, post-save rollback and an
actual revocation wait. This boundary requires validation on its own new head.

Catalog checkpoint `d0cb0a8091a9228946b04af6d528efa4e7c16944` passed
all seven jobs, including Windows installer smoke, in run `38985779631`.
New administration code below is not covered by that checkpoint.

Protected-grant role and assignment authoring is now prepared in separate
authorized application/repository entry points. It does not query legacy Admin
roles or parse fixed role labels for the acting administrator. Actor group
protections bind to the same trusted AccessSubject observation as authority.
Role writes retain the protected administrator, lock target roles against new
assignments, and preserve system-role immutability, revision, assigned-role
permission changes and own-role/group restrictions. Assignment writes retain
target role/permissions, active users and exact Assets through audit commit;
retirement retains the target assignment and preserves the existing last-direct
recovery guard. Authority/proof freshness is checked after blocking operations;
serialization/deadlock conflicts fail closed without retries. Legacy entry
points remain solely for the unconverted runtime and existing compatibility
tests until the complete switch/removal.

New isolated bootstrap-database tests exercise direct and verified-group Admin
role/assignment authoring with no legacy UserRoles, custom security.manage
denial, system/own-role and own-group changes, assigned-role permission edits,
stale revisions, duplicate/invalid scopes, last-direct recovery refusal,
post-save rollback, actual administrator revocation and target-role retirement
lock waits. This batch requires current-head CI before a validation claim.

Administration head `872f7f1d3838b51a5f5e3a5a2a1edf3b64c6469e` passed
all seven jobs in run `38986483093`, including the Windows installer. Its new protected-grant role/assignment
cases executed in the isolated bootstrap database. No runtime authority switch
has occurred.

Identity-only provisioning and directory reads are now prepared with a
UserIdentity record and IUserIdentityRepository/UserIdentityService. They reuse
the existing atomic user/audit upsert without loading or parsing legacy role or
Asset-scope tables. First login adds no grants or legacy roles; bootstrap stays
a separate explicit grant initializer. RequestAccessContext accepts this
identity-only profile and binds the same trusted provider/group observation.
Services are registered but live middleware still uses the old path until the
complete switch. Added database cases cover concurrent same-identity creation,
one provisioning audit, custom labels in the old table, metadata refresh and
disabled identities. New-head CI is required.

Catalog review also preserves idempotent registration across approval-state
changes: identical immutable content returns the current installed trust/enabled
state, never changes approval or re-enables a retired module. A caller's initial
Untrusted classification does not conflict with an already approved copy.
Regression cases cover both approval preservation and disabled-state replay.

## Prepared host identity and grant policies

`GrantIdentityMiddleware` provisions only identity metadata, rejects disabled
users, binds the trusted request subject and invokes the separate bootstrap
repository only after matching the configured normalized account allowlist.
It never issues user, role, permission or Asset wildcard claims. A declined
one-time bootstrap result does not invoke legacy provisioning or recovery.

`GrantPermissionHandler` uses the exact principal bound by RequestAccessContext.
It queries current resource permission prerequisites, DeploymentWide authority
for the module catalog, and protected administrator classification for security
administration. Unknown permissions, missing identity binding, anonymous users
and replacement principals fail closed. Each authorization evaluation queries
again; injected claims and earlier successful results do not supply authority.
Resource predicates and transactional mutation fences remain mandatory.

The host deliberately continues to register the existing middleware/policies
until the complete route/client replacement is ready. These prepared components
introduce no selectable runtime mode. Framework authorization checks cover all
product permissions, spoofed claims, revocation, principal replacement and group
subject/cancellation propagation. Middleware checks cover anonymous, inactive,
ordinary and explicitly allowlisted identities, no authority claims, TestHeader
group separation and a declined repeat bootstrap. Local targeted build passed
with zero warnings/errors; identity boundary and 14 CI convention checks passed.

Identity-directory head `a7d0413781849e800aec4e5518e16fdcaf0f8dc4` passed
PostgreSQL integration and five other jobs in run `38987267817` at this
checkpoint; the Windows foundation/installer job remained in progress.
These results do not validate subsequent host-policy changes.

## Remaining atomic runtime integration

- Bind identity-only provisioning, explicit grant bootstrap, RequestAccessContext,
  dynamic SQL-backed permission policies and /security/me to one grant source.
  Do not copy authority or Asset wildcards from legacy claims.
- Convert every published resource/global/admin route to its prepared entry
  points. Check the raw V1 /executions/shadow route explicitly: its existing
  pre-transaction scope check is not a mutation fence, and caller-supplied
  external mappings must not bypass persisted Asset authority. Preserve V1/V2
  messaging compatibility while resolving that HTTP boundary before activation.
- Return server-calculated per-resource action capabilities (configuration
  management/run and recommendation decisions in particular), using paired
  permissions for the actual targets. Never infer them from coarse menu hints.
- Use identity-only directory records and protected grant administration for
  user/group assignments and custom roles. Remove fixed-role access editing and
  migration-only APIs/UI, then regenerate the client and update route/E2E tests
  together. Leave no published path that still requires legacy Admin claims.
- Run the complete direct/group scope/permission, revocation, frontend loss-of-
  access and seven-job matrix on the final reviewed head and unchanged merge
  tree before merging. Keep #92 the next Phase 1 task after this runtime work.

Do not introduce a Legacy/Observe/Enforce state machine or a production
migration campaign for this unreleased baseline. Do not enable a partial mixed
legacy/grant runtime merely because an individual repository or UI test passes.
User observations and the deferred enum-generation decision remain recorded in
`docs/development/decision-followups.md`; they do not automatically change the
active enterprise RBAC priority.
