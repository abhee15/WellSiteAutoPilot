# Scoped authorization cutover checklist

**Status:** unified grant API/frontend integration merged via PR #127 as d23ef0d
after all seven jobs passed head 1a13a4d in run 39004825569. Real Windows browser
and domain validation remain release gate #92.

## Baseline and constraints

- The API binds a persisted identity to a trusted request context and uses paired
  grants for policies, resource reads and transactionally fenced mutations. Legacy
  roles, permission claims and Asset wildcard unions do not authorize HTTP routes.
- The new grant/role SQL queries are diagnostic and must agree with the pure
  scoped-grant evaluator. Any SQL/domain disagreement blocks cutover.
- Every resource decision must use a **single active direct-user or verified Windows-group role grant**
  containing both the permission and the Asset scope; do not union permissions
  from one grant with scopes from another. The native enabled-token resolver and paired group evaluation are implemented;
  live group authorization still requires the complete #76/#77 runtime boundary.
- Role or Asset inactivity, malformed/duplicate grants, unknown permissions,
  absent Assets, unverified identities, and concurrent revisions must fail
  closed. `AllAssets` is explicit, not implied by a global permission.
- The legacy-grant materialization and its audit-provenance rollback are
  **not** an authorization switch. Their successful results do not approve
  new runtime semantics.

## Required HTTP coverage

| Route class | Permission | Exact enforcement requirement |
| --- | --- | --- |
| GET /assets and /assets/{id} | assets.read | Filter at SQL source before pagination; exact Asset checks for detail |
| POST /assets with parent | assets.manage | Check parent Asset under the same resource permission before creating |
| POST /assets at root | assets.manage + explicit admin rule | Do not infer universal rights from one scoped asset |
| POST /asset-types; GET /asset-types | assets.manage / assets.read | Create requires protected administration plus explicit AllAssets management. Full catalog reads require protected administration plus AllAssets read; scoped readers see only types used by readable Assets |
| GET /configured-logic and /configured-logic/{id} | configured-logic.read | Filter by **every bound Asset in every disclosed revision** before limit/response |
| POST /configured-logic | configured-logic.manage | Verify all distinct Asset bindings and associated data-binding Assets; reject empty/unproven scopes |
| POST configured-logic revision | configured-logic.manage | Authorize existing revision Assets AND requested new Assets; forbid changing from an authorized revision to an unauthorized one |
| Validate/activate a revision | configured-logic.manage | Re-resolve all revision bindings from persistence at action time; no stale client-supplied Asset list |
| GET /executions and /executions/{id} | executions.read | Filter before pagination; include all V1 Asset and V2 execution Asset scopes |
| POST configured-logic revision Shadow | executions.request + configured-logic.read | Build the snapshot from the persisted Active Shadow revision; retain complete read/request scope through execution/outbox/audit commit and recheck receipt replay |
| GET /recommendations and /recommendations/{id} | recommendations.read | Filter by persisted target Asset before paging or materializing a response |
| POST /recommendations/{id}/decision | recommendations.decide | Resolve target Asset authoritatively and enforce permission before any status mutation |
| /logic-modules catalog, including package upload, approve and retire | logic.read / logic.manage | SQL-backed **deployment-wide** permission; do not satisfy using AllAssets grants. Approve and retire lock the module row, recheck authority after the lock, require a reason and write one audit event; a retired version is never approved again |
| /security/** administration | security.manage | DB-revalidate an active administrator; no self elevation, last-admin removal or audit bypass |

Map every published API route (including future ones) to a permission and
scope-kind in tests. Reject unknown or unmapped protected route categories.

## MVP implementation and replacement

The 2026-10-16 revision of [ADR-006 section 8](../adr/ADR-006-enterprise-entitlements.md)
supersedes the earlier production rollout plan. Complete one grant-based
runtime, validate it, and remove obsolete legacy authority. No persistent
`Legacy`/`Observe`/`Enforce` state machine, fleet policy-revision protocol,
whole-deployment migration campaign or rollback administration UI is needed
before the release baseline. An explicit development database reset and updated
seeds are acceptable; never reset a database automatically.

1. Resolve a verified authenticated identity to an active persisted user.
2. Apply one paired-grant model to coarse policies, global operations,
   authoritative resource checks, collection SQL and capability hints.
3. Fence mutation authorization with revocation/role edits in the database;
   request-start claims alone do not protect a later write.
4. Complete bootstrap, own-grant and last-administrator protection against
   the grant model, then remove legacy authority and migration-only UI/API.
5. Run deterministic API/security coverage and all seven exact-head CI jobs.
   Real Windows authentication remains release-readiness issue #92, not proof
   supplied by TestHeader or installer smoke.

The coordinated runtime replacement is implemented on the working branch.
Validate its full route, permission, direct/group and frontend revocation matrix
before merging; no mixed legacy/grant runtime mode is introduced.

## Required negative tests before enforcement merge

- Two users and two or more Assets; no permissions/scope union across
  different grants, or across historical and newly created grants.
- SQL and pure evaluator parity for *every* product permission, including
  global-only roles, mixed roles and explicit AllAssets.
- Missing/inactive/moved Assets, inactive users, revoked/expired grants,
  role permission revision changes, unknown or malformed grant types.
- Bulk APIs must reject empty/duplicate/partially unauthorized Asset sets.
- No unauthorized results before or after pagination, including V2 executions
  and Configured Logic with revisions containing mixed Asset scopes.
- Writes must verify authoritative targets inside a transaction or an
  equivalent concurrency boundary, including races with revocation.
- Last-administrator and own-grant protections; actor identity is persisted
  and non-spoofable in audit; denied decisions do not mutate anything.
- Grant/role mutations and bootstrap are atomic and auditable; concurrent
  and uncertain network outcomes fail closed without blind write retries.
- GitHub PostgreSQL/NATS tests and successful NSIS installer smoke tests
  on the exact head; genuine Windows authentication integration remains
  the separate release-readiness gate #92.


## Route inventory

The reviewed route/method/policy inventory lives in
[`tools/ci/check_api_authorization.py`](../../tools/ci/check_api_authorization.py).
It includes user/group assignment administration, Asset/Configured Logic
capabilities and configured Shadow submission. Keep that executable inventory
current in each API change rather than duplicating endpoint counts here.

The guard detects policy metadata changes; it does not prove resource
scope, revocation safety or SQL filtering before pagination. Integration tests
must establish those properties for every protected family.

## Extraction and enforcement remain separate

PR #79 was extracted through #102–#115 and closed unmerged, with its original
branch preserved. See [the extraction ledger](scoped-access-extraction-ledger.md).
Do not merge its stale branch over subsequent fixes.

Runtime enforcement remains open under #76. Frontend milestone #80 is complete;
directory group verification remains #77. The migration diagnostics already
landed are not prerequisites for the simplified MVP replacement and should be
removed with the obsolete authority when the replacement is complete.

## Direct administrator recovery preparation (2026-10-16)

Grant retirement preserves the last active direct recovery administrator. A
recovery assignment is a valid deployment-wide grant of the active protected
built-in `admin` role (including `security.manage`) to an active local user,
with no Asset bindings. Legacy roles, group grants, custom roles, inactive
users/grants and Asset-scoped or malformed assignments are not recovery
substitutes. The remaining-administrator query, compare-and-set retirement and
audit share a serializable transaction; concurrent retirement of two distinct
administrators cannot commit both removals. The structured 409 refusal is
`SECURITY_LAST_DIRECT_ADMIN_REQUIRED`; the UI explains how to assign another
direct administrator and requires a fresh read without automatic retry.

This protects assignment retirement during preparation. It does not provision
the first grant-based administrator or switch runtime authority. Complete
bootstrap/reseeding, group-aware self-change checks, all runtime policies,
resource mutations and capability hints before removing legacy authority.
Future user deactivation or recovery-role mutation paths must preserve this
same invariant; the built-in role is currently immutable and there is no
public user-deactivation API.

## Current-administrator write/revocation boundary

Role creation/update and direct/group grant creation/retirement revalidate the
persisted active administrator inside the mutation transaction. The guard
locks both the active user row and its direct legacy Admin assignment with
`FOR SHARE`. Concurrent account deactivation or assignment deletion must wait
until the authorized write commits; if revocation wins first, the mutation is
denied or conflicted without automatic retry. A service check or serializable
snapshot alone is not a substitute for holding the authority rows through the
write. Roles/grants and their audits remain atomic.

The regression matrix observes actual PostgreSQL lock waits for all four write
paths against both role removal and user deactivation, then rejects direct
repository writes from the revoked actor without new records or audits.

This guard protects the **current** administrator authority. It does not wire
resource operations to proposed grants. At complete cutover, replace the
legacy authority predicate with the paired direct/verified-group grants and
apply the same held-authority/target boundary to every protected mutation;
remove obsolete legacy administration paths together. Do not mix legacy scope
claims with newly effective grant permissions.
