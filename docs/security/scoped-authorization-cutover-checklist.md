# Scoped authorization cutover checklist

**Status:** planning and diagnostic implementation only. **Do not enable or merge
an enforcement switch based solely on this document or passing migration CI.**

## Baseline and constraints

- Legacy Windows identity, persisted legacy roles, permission claims, and Asset
  scopes remain the live authorization source until a separately reviewed,
  transactionally fenced cutover.
- The new grant/role SQL queries are diagnostic and must agree with the pure
  scoped-grant evaluator. Any SQL/domain disagreement blocks cutover.
- Every resource decision must use a **single active direct-user role grant**
  containing both the permission and the Asset scope; do not union permissions
  from one grant with scopes from another. Windows group SID grants cannot
  authorize until a trusted resolver and issue #77 are complete.
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
| POST /asset-types; GET /asset-types | current assets.manage / assets.read | **Design decision required:** type catalog is deployment-wide without an Asset. Keep a strict admin-only bridge until a dedicated global catalog permission is introduced; never infer a universal scope |
| GET /configured-logic and /configured-logic/{id} | configured-logic.read | Filter by **every bound Asset in every disclosed revision** before limit/response |
| POST /configured-logic | configured-logic.manage | Verify all distinct Asset bindings and associated data-binding Assets; reject empty/unproven scopes |
| POST configured-logic revision | configured-logic.manage | Authorize existing revision Assets AND requested new Assets; forbid changing from an authorized revision to an unauthorized one |
| Validate/activate a revision | configured-logic.manage | Re-resolve all revision bindings from persistence at action time; no stale client-supplied Asset list |
| GET /executions and /executions/{id} | executions.read | Filter before pagination; include all V1 Asset and V2 execution Asset scopes |
| POST /executions/shadow | executions.request | Authorize the target Asset before writing an execution/outbox request; no cross-scope execution |
| GET /recommendations and /recommendations/{id} | recommendations.read | Filter by persisted target Asset before paging or materializing a response |
| POST /recommendations/{id}/decision | recommendations.decide | Resolve target Asset authoritatively and enforce permission before any status mutation |
| /logic-modules catalog | logic.read / logic.manage | SQL-backed **deployment-wide** permission; do not satisfy using AllAssets grants |
| /security/** administration | security.manage | DB-revalidate an active administrator; no self elevation, last-admin removal or audit bypass |

Map every published API route (including future ones) to a permission and
scope-kind in tests. Reject unknown or unmapped protected route categories.

## Enforcement and deployment state

1. Complete authoritative identity/provider integration. Windows groups remain
   unsupported until trusted SID validation.
2. Implement authorization policy and resource resolution in one coherent
   request pipeline, rather than layering an Asset check *after* a legacy
   policy that already denied all custom-grant-only users.
3. Implement read-only observation with low-cardinality disagreement metrics
   and no sensitive request payloads. An observer must never grant access.
4. Perform whole-deployment legacy/proposed parity verification, including
   inactive-user reactivation semantics and cohorts larger than the current
   diagnostic bounds (100 users, 200 roles, 2,000 Assets).
5. Design a durable, revision-fenced state machine (`Legacy`, `Observe`,
   `Enforce`) with explicit administrator confirmation and independent
   rollback. A partially deployed cluster must not mix ungoverned decisions.
6. Roll out only after the API-level and Windows-native negative suites are
   passing; preserve a documented, tested rollback to the previously valid
   policy without deleting audit events or user-authored grants.

## Required negative tests before PR #79 merge

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
- Confirmed migration and rollback are atomic and idempotent; unconfirmed,
  concurrent, and uncertain network outcomes fail closed.
- Genuine Windows auth/native TeamCity integration alongside GitHub Linux
  PostgreSQL/NATS tests and successful NSIS installer smoke tests.

## Current work separation

- PR #79 builds the new permission model while keeping production auth legacy.
- Separately merged execution-dispatch PR #81 is already on `develop`.
  Integrate `develop` into the feature branch and resolve differences
  carefully **before** PR #79 merge, not as a blind conflict overwrite.
- Issue #80 is the planned frontend-first end-to-end operator workflow after
  security enforcement is complete.
