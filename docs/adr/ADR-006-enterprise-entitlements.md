# ADR-006: Configurable enterprise entitlements and directory-group integration

## Status

**Proposed for implementation.** Design baseline for [enterprise access initiative #73](https://github.com/abhee15/WellSiteAutoPilot/issues/73). No runtime security behavior changes until a verified cutover. Revisit choices with testable evidence; changes require a new ADR or an explicit revision.

## Context

WellSite AutoPilot initially provisions authenticated Windows users identified by a stable Windows SID when present (otherwise normalized name), and stores individual `user_roles` plus `user_asset_scopes`. The `ApplicationRole` enum (`Admin`, `Engineer`, `Operator`), `RolePermissionMap`, and `WellSitePermissions` are compiled. `UserAccessClaimsMiddleware` currently generates role/permission/Asset-scope claims from a user's persisted direct assignments; route policies check broad permission claims, while `AssetScopeAuthorizer` separately checks Asset IDs. There is no user/group directory lookup or group grant persistence.

Enterprise deployments require customer-defined roles, AD/Windows group assignment, delegated security management, auditable access previews and prompt revocation. Simply adding group roles to the user's roles and group Assets to their independent Asset list creates a **cross-product privilege escalation**. For example, `Engineer@WellA` plus `Operator@WellB` must **not** permit `Engineer@WellB`.

The product is on premises (normally without Internet), runs by a CygNet Bastion/server, has service-executed Logic Modules, and may eventually authorize physical control. Authentication and business authorization must remain distinct and have safe failure modes.

## Decision

### 1. Product-owned permission catalog

- Define stable, namespaced permission identifiers (the currently published 11 identifiers remain valid during migration). A permission corresponds to an implemented server capability; **customers cannot invent executable permissions** by entering new strings.
- Each permission has a stable key, localization key, group/category, description, scope kind (`ResourceScoped` or `DeploymentWide`), privilege class (`Ordinary`, `SecurityAdministration`, `ControlCritical`), introduced/deprecated version and optional superseding identifier.
- Route policies provide coarse permission prerequisites; a single centralized evaluator provides **resource-specific** grant checks on every sensitive operation. Clients never grant access.
- New security administration capabilities receive narrower product permissions (for example `security.roles.manage`, `security.directory-groups.manage`, `security.assignments.manage`, `security.audit.read`). The existing `security.manage` is bridged safely during migration, not silently reinterpreted.
- Distinguish Shadow request, Recommendation decision, autonomous-mode enablement and physical command approval. One grant does not imply the others by naming convention.

### 2. Built-in system templates and customer roles

- Seed immutable, enabled built-in **Admin, Engineer, Operator** definitions with stable IDs/keys and known permission sets. Built-ins are marked as system-owned and cannot be renamed, deleted, or edited in place. Admin is especially protected.
- Customers can clone a built-in or create roles with names, descriptions and a checked subset of the supported permission catalog; roles have stable immutable IDs/keys, `IsSystem`, `IsActive`, a concurrency revision and audit metadata.
- Customer-defined roles may be edited, deactivated and retired. Retirement never silently deletes audit history or orphaned assignments. Changes affecting active assignments must display an impact/permission diff and use compare-and-swap revisions.
- Roles define *what operations* are allowed, **not which Assets**; Asset eligibility belongs to each assignment.

### 3. Scoped access assignments (the security invariant)

Use explicit `AccessAssignment` records:

| Field | Meaning |
| --- | --- |
| `Id` | Stable grant/assignment ID |
| `SubjectKind` | `User` or `WindowsGroup` |
| `SubjectKey` | Stable local User ID or canonical, validated Windows group SID |
| `RoleId` | Existing, active role definition |
| `ScopeKind` | `ExactAssets` or explicitly approved `DeploymentWide` |
| `AssetIds` | Nonempty exact Asset ID snapshot where required |
| `IsActive`, `Revision`, timestamps, actor | Effective state, concurrency and audit |
| `Origin` / provenance | Local/direct, directory-group mapping or privileged bootstrap |

Core predicate:

`Allow(subject, permission, targetAsset, context)` **iff** the authenticated, active subject has at least **one** active, trustworthy assignment whose role contains `permission`, whose exact scope includes `targetAsset` (for resource operations), and whose privilege class/context permits that operation.

- **Never** first union roles from every assignment and separately union Asset scopes. The role and its Asset restriction stay together.
- Multiple grants are additive only **as complete tuples**. No initial negative-grant ACLs, inheritance or arbitrary custom policy language.
- A `DeploymentWide` assignment is explicit, audited, and requires a privileged actor; a null/empty Asset collection must never mean global access.
- Asset scope expansion to descendants is **not** automatic. ADR for #65 must explicitly specify bound Asset visibility, execution fan-out, effective descendant authorization and reparenting. Default remains exact Asset IDs.
- A deployment-wide permission (`security.roles.manage`, etc.) cannot be conferred by a Well-scoped grant; enforce permission type/scope compatibility at write time **and** evaluation time.
- `User.IsActive = false` denies access even when a Windows group would otherwise grant it.
- Unknown role IDs, unknown permission keys, unsupported provider identities, invalid scope nodes, inconsistent versions and stale/untrusted group proof fail closed.

### 4. Group resolution provider, Windows authentication and identity

- Retain Windows Negotiate for V1 authentication. Use Windows user SID as stable identity where supplied. Group mappings are keyed by canonical Windows **security-group SID**, not renameable `DOMAIN\\Name`.
- Introduce a small `IIdentityGroupResolver` provider boundary returning the authenticated user's verified group SIDs, assertion provenance/time/freshness, directory health and diagnostics. Do **not** tie Domain/Application projects to `System.DirectoryServices`, Windows-only APIs or LDAP package implementations.
- On supported Windows deployments, first evaluate validated group SIDs from the authenticated Windows security principal/token. Do not trust client-supplied headers or UI role/group names. Directory lookup/LDAP is an explicitly configured **optional** adapter for group search, name resolution, nested membership or topology without suitable group claims.
- Nesting/transitive group membership and cross-domain/trust behavior must be specified, bounded and tested. Avoid direct network LDAP queries on every browser request or unbounded nested graph traversal. Host/service identities must have only required directory-read capabilities.
- Directory lookup, credential handling and secure transport configuration remain local/on-prem. A directory is an identity provider, **never** the authority that assigns product permissions itself.
- Normal authenticated identities may be upserted individually for audit and preferences; they receive no automatic permission when not covered by explicit direct/group entitlements. Persist group **mapping configuration**, not indefinite individual replicated AD group membership as authorization truth.

### 5. Staleness, outages and revocation

- Define separately: Windows session/token lifetime, group-membership freshness, configured role/assignment revision, API authorization cache TTL and critical-operation revocation.
- Local role and assignment edits invalidate effective authorization promptly across server nodes. Changes use a versioned cache key or invalidation and are checked at sensitive execution/ControlAction dispatch.
- Directory-unavailable behavior **may not extend previously untrusted or expired group-based privileges**. A bounded, explicit cached membership proof may be used only under an approved noncritical policy; stale/unknown membership **fails closed** for security administration and autonomous/physical control. Never silently substitute an empty group set as an administrator grant.
- Distinguish directory outage from an authenticated session expiry, and return stable diagnostic codes; do not leak directory topology, user group lists or credentials.
- Preserve durable execution/service-account boundaries: a Logic Module does not inherit the interactive user's Windows token. Any human authorization grant attached to a requested execution is a bounded audited capability, rechecked at relevant safety gates.

### 6. Administration and UX

Access administration lives in a privileged **Administration** area, consistent with the current user menu positioning; no general operator navigation entry or direct editable DB surface.

Proposed screens:
1. **Roles**: Built-in and custom role list, search/filter, clear system/custom badge in page (not generalized operational warnings), create from scratch or clone, editable localized role display name/description, grouped permission matrix, privilege warnings, version information, dependency references and deactivate/retire.
2. **Directory groups**: Search and select verified AD groups when directory browsing is configured; show SID and friendly name, unmapped vs mapped, directory source/freshness and health. Permit manual validated SID mapping with a deliberate confirmation when search is unavailable.
3. **Assignments**: Choose user/group, role, and explicit Asset scope together; review per-assignment grant, control-sensitive permissions and deployment-wide impact.
4. **Effective access inspector**: Per-user user/group-derived permissions with provenance, *per-Asset* scope and "why allowed/denied" information; never display a false combined role-plus-scope union. Clearly label stale/unknown directory state.
5. **Change review**: Save with optimistic concurrency, before/after diff, number of potentially affected users (when known), audit reason for high-privilege changes and stable correlation ID. Never auto-retry ambiguous writes.

Administrative delegation:
- Initially only protected active administrators can manage roles, groups and assignments. Future explicit security-management permissions may delegate narrowly. Editing others' grants **must not** allow self-elevation by changing a role assigned to oneself, a group one belongs to, or a higher-privilege role. Role edits need full downstream affected-user analysis and an authorization ceiling.
- Separate "manage role definitions" from "assign elevated roles" and "manage group mappings" so authoring a role is not equivalent to granting oneself system access.
- Protect at least one directly assigned active break-glass administrative principal against accidental removal; do not rely **only** on AD group membership that could disappear or become unreachable. Document customer-owned credential recovery securely (no default password or backdoor).

### 7. Persistence and concurrency

Tentative tables (final names after EF design review):

- `security.role_definitions`: stable ID/key, name, system/active flags, version, created/updated UTC, author.
- `security.role_permissions`: Role ID + product-defined permission key, unique key.
- `security.directory_groups`: canonical SID, normalized display name, directory/provider identifier, mapping state.
- `security.access_assignments`: subject kind/key, Role ID, explicit scope kind, status, version, actor/time/audit reason.
- `security.assignment_asset_scopes`: grant ID + exact Asset ID, unique composite key.

Indexes for principal/group lookup and role activity, foreign keys preventing orphan scopes, check constraints for assignment kinds and explicit global grants where supported, versioned/transactional mutations. Directory SID uniqueness must account for provider namespace/domain trusts. Use EF Core migrations via existing offline migrator, never server-startup migration.

Audit grant/role creation, edits, mapping, retirement, directory configuration, bootstrap, privileged effective-access diagnostics, and denied security-changing attempts with stable codes and correlation IDs. Audit must not embed credentials, full directory contents or module outputs.

### 8. Safe migration from fixed direct roles

1. Introduce new schema, stable product permission catalog, protected default-role rows and a **read-only** new entitlement evaluator, with differential tests; keep current production path intact.
2. Convert each existing direct user's `user_roles` and `user_asset_scopes` to distinct role-scope grants preserving that user's currently effective **role × scope** semantics, including explicit deployment-wide protected Admin grants. Do not infer unexpected descendant Assets or introduce implicit group grants.
3. Verify per-user/per-permission/per-Asset equivalence across fixtures and real upgrade snapshots, including direct users with multiple roles. Report unknown/invalid source roles rather than silently granting them.
4. Stage the new policy evaluator behind an explicit safe rollout with telemetry (decisions/provenance, never secret payloads); update coarse route checks and all scoped queries to consume **paired** grants. A globally flattened Asset list remains unsuitable for resource-aware permissions.
5. Migrate current admin users listing and `ReplaceAccessAsync` endpoint via backward-compatible new contracts. New APIs may be additive in REST V1; incompatible changes require new major API version per ADR-004. Generated TS is never manually edited.
6. Switch to a single authoritative evaluator only after verified parity; remove legacy enum/map as authorization sources in a controlled follow-on migration. Preserve audit and rollback/upgrade safety.

Do not layer Windows-group grants into the current global `RolePermissionMap`/Asset list as an intermediate live authorization implementation.

## Required acceptance tests

1. `Engineer@WellA + Operator@WellB` permits Engineer permissions **only** on Well A.
2. User/direct + one group + two groups + mixed privilege assignments never cross-combine role and Asset restrictions.
3. Empty, unknown or inactive user/group/role/Asset; invalid SID; no permission mapping; forbidden global assignment; missing directory proof all fail closed.
4. Direct-user deactivation, role deactivation and AD group revocation remove effective access within documented freshness limits; critical actions cannot rely on expired group membership.
5. Only properly privileged administrators can create/clone/edit/retire roles or map groups; no self-escalation, group-mediated indirect self-escalation or last-admin removal.
6. Simultaneous edits yield one accepted compare-and-swap change and a visible conflict. Every accepted change commits its audit event transactionally.
7. Supported nested groups, renameable names/stable SID, directory outage, cross-domain/trust scenarios and Windows service host identities behave deterministically.
8. Query/list/claim restrictions apply **before** pagination; direct/group provenance and per-Asset effective access are correct across backend API and frontend.
9. Upgrade differential tests preserve legacy authorization and do not grant descendants implicitly.
10. Authorization of autonomous mode and physical control remains independently gated by readiness, interlocks, identity/grant freshness and operational ownership.

## Implementation slices and quality gates

Deliver cohesive end-to-end PRs, each with backend/service/persistence/API/frontend/testing where relevant:
- **Catalog and role authoring**: seeded system roles, product permission metadata, guarded CRUD/clone, persisted revisions/audits, Admin UI, contract and security tests.
- **Scoped assignments + evaluator**: direct user assignment UI/API, resource-aware policy checks, strict equivalence rollout.
- **Windows group assignments**: token SID resolution, optional directory browser adapter, group mapping UI, freshness controls, outage tests.
- **Cutover and hardening**: migrate legacy endpoints, verify nested membership and upgrade safety, audit/impact preview, enterprise CI tests.

Every security API change receives negative permission tests (including non-Admin callers), cross-scope test fixtures, optimistic concurrency tests and audit checks; CI verifies PostgreSQL migration, Windows-host install, API/React and engineering conventions.

## Trade-offs / open details

- Initial single-customer/deployment tenancy; do not introduce speculative cloud multi-tenancy.
- Exact Assets only initially; future descendant mode depends on #65 design.
- Group scope proof freshness and fail-closed policy must be validated with target customer Windows/AD infrastructure and deployment account models.
- Highly privileged group mappings may require two-person approval at customers' option; do not assume a second approver exists in V1.
- Avoid storing arbitrary customer-generated role expressions or scripts. Introduce conditional policy languages only when concrete requirements demand them.

## Alternatives rejected

- **Hard-code more enums and assign AD groups into existing `user_roles`**: new roles require redeploy and can cross-combine unrelated Asset scopes.
- **Grant permissions solely from AD group names**: renames, trusts, lack of product permission governance, and audit gaps.
- **Treat role edit access as permission to elevate self**: unacceptable in an autonomous industrial control system.
- **Require live LDAP for every operation**: fragile on isolated runtime nodes, high latency and incompatible with outage-safe execution.
- **Full external IAM/ABAC policy engine in V1**: unnecessarily complex for a single-site on-prem modular monolith and difficult to support offline.

## Related

- Enterprise initiative: #73
- Existing Asset hierarchy execution semantics: #65
- Repository conventions and maintainability: #69
- ADR-001 service boundaries; ADR-002 persistence; ADR-004 API versioning.
