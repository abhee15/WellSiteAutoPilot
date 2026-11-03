# User access directory: UX refinement and provider-neutral access inspection

**Status:** Agreed UX direction and implementation plan. **No UI, API, authorization, persistence, or identity-provider behavior has been implemented by this document.** Implement in independently reviewable PRs against develop and retain CI/real-Windows acceptance gates.

**Context:** The current Administration > Users screen has a standalone “Only users with no direct role” checkbox. The value comes from activeDirectGrantCount and the server's awaitingAccess=true query, which selects active users with no usable direct grant. The filter is useful for auditing individual assignments, but it **does not** identify everyone who lacks effective access: an authenticated user can receive scoped grants through verified directory-group membership.

## Goals and non-goals

Goals:
- Give administrators a clear answer to **who the user is**, **which assignments are configured**, **what access was actually verified**, and **where that access came from**.
- Treat an individual user's direct assignments differently from effective permissions obtained through verified groups; never label the former as “waiting for access”.
- Fit existing Administration, list/detail, filtering, security and localization conventions instead of inventing another component system.
- Remain identity-provider neutral: Windows AD first, later trusted OIDC/Entra or other providers only when authentication and membership evidence adapters are explicitly supported.
- Improve finding users in larger deployments without filtering only a 100-row client snapshot; preserve authorization, pagination correctness and stable selection.

Non-goals:
- Do not implement OIDC/Entra authentication, SCIM provisioning or an identity-management administration console.
- Do not introduce an alternative authorization evaluator, persist an unbounded directory membership replica, grant a role on sign-in, or infer access from readable group names.
- Do not change the paired-grant model, runtime policies, role definitions, recovery administrator or active execution/service identity boundaries.
- Do not introduce an “Awaiting access” metric or filter until the system can establish an appropriately complete, fresh, authoritative assessment.

## Verified current state (develop baseline)

- AdministrationWorkspace exposes Users, Roles, Grants and Activity at #access; “Assign role” opens the existing Grants editor with a selected user.
- AccessManagementPage uses loadSecurityUsers and currently renders identity, number of direct grants (“Direct roles”), account state, last seen, and an Assign role action. A standalone checkbox maps to awaitingAccessOnly.
- GET /api/v1/security/users?limit=100&awaitingAccess=true is a protected server-side filter for **active users with no active direct grant to an active role**. It is applied before the 100-row limit, newest seen first. It cannot report membership-derived grants.
- activeDirectGrantCount is a count of direct **grant records** (which can repeat a role at different scopes), not necessarily the number of distinct role definitions. Rename the UI concept to **Direct assignments**; preserve the existing count's semantics.
- Group grants use a directory namespace plus an opaque group ID, and trusted membership evidence is separate from directory-name browsing. Group membership is not provisioned as individual direct grants.
- The directory list is bounded at 100; client-only search or filtering over that first page would provide incomplete results.
- A user's own sign-in permissions reflect trusted runtime evaluation. The Users directory currently has no authoritative, complete, per-user effective-access list/verification response for arbitrary accounts.

Relevant code: src/web/src/views/AccessManagementPage.tsx; src/web/src/views/accessManagement.ts; src/web/src/views/AdministrationWorkspace.tsx; src/web/src/views/GrantAdministrationPage.tsx; src/web/src/views/DirectoryGroupPicker.tsx; src/web/src/components/FilterToolbar.tsx; src/web/src/components/SearchInput.tsx; src/web/src/security/sessionAccess.ts.

Architectural baselines: docs/adr/ADR-006-enterprise-entitlements.md; docs/development/grant-runtime-initialization.md; docs/security/scoped-authorization-cutover-checklist.md; docs/design/ui-conventions.md. PR #137 introduced the current filter; PR #313 established provider-neutral directory-group grants.

## Information architecture and copy

Keep the existing Administration tabs: **Users | Roles | Grants | Activity**.

### Users directory (default)

- A single **User access directory** panel with header and icon-only Refresh, using ActionButton.
- Directly below: shared FilterToolbar with SearchInput (“Search users by name or account”) and one **Filters** control; use applied-filter chips, “Clear filters”, and a trustworthy result count or “showing N” when the total is unknown.
- Columns at the initial delivery stage: **User** (display name + secondary account identifier), **Direct assignments**, **Account status**, **Last seen**. Identity provider/source appears only when an authoritative server field is available; do not guess it from an account string.
- Clicking the user name/row opens that user's read-only details, while other controls retain their own action semantics; no confusing separate per-row Inspect column.
- Keep **Manage assignments** as an explicit action in details, leading to the existing Grants workflow. No implicit mutation, and do not permit self-assignment or inactive-user assignment.
- The standalone checkbox is **removed**. Under Filters show **Direct assignments: All / With direct assignments / Without direct assignments**. The last option means *zero active direct paired grants*, not “without any access”.
- Account status: All / Active / Inactive. Search and filters compose; all filtering is applied on the server before paging. Identity-source filtering is deferred until the server can provide reliable provider identity.
- No “Effective access” column or user-access verdict in this first delivery: the data does not yet exist. The current misleading explanatory paragraph is replaced by a short contextual helper in the filter/empty state: “Group-based access is not included in direct assignments.”

### User detail (second delivery)

- Keep a single selected-user details card, within the shared master/detail layout (2:3 list-to-details ratio on wide screens; replace list with detail on phones).
- Header: display name, account identifier, account status, last seen; provider identity/source only when verified and supported.
- **Direct assignments** section lists active and relevant retired grants with role, exact assigned scope (DeploymentWide / AllAssets / ExactAssets), and name-resolved assets where authorized. Reuse existing grant read/model; zero reads as “No direct assignments”.
- **Group-derived assignments** must be explicitly unavailable or “Not evaluated” until a trusted per-user membership assessment exists. Do not display configured group grants as though user membership has been proven.
- **Effective access** is a later progressive-disclosure section: summary, verification timestamp/source/freshness, permission-and-asset combinations, and explainable allow/deny evidence. No placeholder green status, fabricated role, or fake zero.
- Primary detail action: **Manage assignments**, routed through existing protected Grants tab with preselected user and preserving the reviewed security mutation process. Refresh/cancel/selection use existing standard controls.
- Use accessible names and correct focus restoration for selection, close, back navigation, filters and errors. Support narrow widths (320, 390, 736) and desktop (1440 and larger). Avoid nested scroll regions.

Illustrative anatomy, **not currently implemented**:

    User access directory                             Refresh
    [ Search users by name or account ] [ Filters ]
    [ Without direct assignments × ] [ Clear filters ]
    ----------------------------------------------------------------
    User                 Direct assignments  Account   Last seen
    Example A            None                Active    Yesterday
    Example B            2                   Active    Today

    Selected user: Example A
    Account: CORP\example-a | Active
    Direct assignments: None
    Group-derived assignments: Not evaluated
    [ Manage assignments ]

When verified evidence becomes available, show “Group-derived assignments: Operator on A/B (via Field Operators, verified at [timestamp])” rather than converting group access into a direct-assignment count.

### Empty, loading and permission states

- “No matching users” means no matching result **after** server-side filtering, not no matches among the first 100 loaded.
- “No users recorded” is distinct from “No matching users”; offer Clear filters for filtered emptiness.
- Failed list/detail reads keep an explicit error state; forbidden reads clear protected data and do not show stale access claims.
- A zero direct-assignment count is neutral, not a warning or waiting-access alert.
- Disabled identities remain visible if authorized by the account-status filter, but cannot receive the existing protected Assign action through this screen.

## Provider-neutral model and verification semantics

Use the existing server-owned paired-grant evaluator. Providers supply **trusted identity and membership evidence**, not product role permissions. The **Windows AD adapter** can verify token group IDs; a future OIDC adapter must account for incomplete group claims, claim overage and provider-specific provenance. An administrator's own membership does not prove another user's membership.

Separate these concepts in the view model:

1. **Identity:** stable local user ID; provider namespace and provider-specific opaque identity ID when known; display/account name is cosmetic.
2. **Direct assignments:** product-owned active direct paired grants and asset scopes.
3. **Group assignments:** configured grants for a directory namespace + group ID. They apply to a user only after trusted membership proof is available.
4. **Evidence state:** NotEvaluated, VerifiedComplete, Incomplete, Stale, or Unavailable, with verifiedAtUtc, expiration/freshness policy, provider provenance and correlation. No stored raw credentials or full directory dump.
5. **Effective authorization:** an explanation produced by the **same runtime evaluator**, tied to the requested permission and exact resource scope; do not union a role from one grant with assets from another.

UI labels derived from evidence:
- **Verified access**: a fresh, complete and authoritative assessment proves at least one usable paired grant in the relevant requested context.
- **Verified no access**: a fresh, complete and authoritative assessment proves no usable grants for the examined scope/permission context.
- **Not evaluated / Incomplete / Stale / Unavailable**: no affirmative assertion about access. When evidence is unavailable, this is **not** interchangeable with “no effective access”.
- **Inactive account**: a separate explicit state; runtime denies access even if configured grants or old evidence exist.

For a user other than the currently authenticated principal, do **not** compute membership by copying the administrator's token or guessing from group names. A future privileged inspector must explicitly identify whether it can obtain fresh, authoritative proof for that particular user, subject to provider policy. If it cannot, return Unknown/NotEvaluated. Prohibit any “live effective access for all users” projection based on incomplete token evidence.

Per-user diagnostics should be administrator-only and protected by current security.manage plus durable read/audit rules (or any later explicitly approved delegated permission). Follow existing guard/revocation fences. Critical writes, control actions and grants continue to use live server authority rather than UI diagnostics, and unsupported/stale evidence fails closed where policy requires.

Never add a global “users without effective access” filter unless the provider and evaluation contract can establish completeness and freshness for the entire targeted result set. If not, provide a **selected-user inspector** rather than a deceptive global list filter.

## Read contract and pagination plan

Phase 1 adds only an administrator-protected **directory read-query** improvement (no authorization rule changes):

- Add validated search term (bounded normalized input; match account/display name), account status, and direct-assignment filter, all applied in SQL **before** pagination.
- Use a deterministic ordering and opaque next cursor or equivalent stable server paging; never infer totals from a capped list, and avoid shifting selection on concurrent sign-ins.
- Semantics of Direct assignments = None must mean no active usable direct paired grant, independently of Active/Inactive account filtering. The current awaitingAccess=true implies active accounts and cannot silently implement the new orthogonal filter for inactive users. Keep as a compatibility alias during migration if needed; document the exact semantics and remove it once clients have migrated.
- Preserve current admin-only authorization; reject invalid query values; avoid unbounded broad identity queries and leak-free query errors. Search only persisted identities known to WellSite AutoPilot, not arbitrary directory accounts.
- Keep a chosen user address/selection stable across pagination, filtering and refresh. A specific authorized detail query is the authority; aging out of a page does not prove a user cannot be read.
- Do not introduce provider-source filtering until source data is authoritative, indexed and tested.

Subsequent read models:
- User-details read reuses authorized user identity and direct-grant endpoints.
- Optional privileged effective-access inspector read takes **one selected local user and explicit permission/resource context**, evaluates trusted evidence and grants, and returns normalized explanation records plus evidence/verification state. It does not mutate rights or authorize actions.
- Future provider adapters contribute evidence under a common contract without introducing a Windows-only field into the shared user directory.

## Incremental implementation slices

### Slice 1 — Directory truthfulness, search and filters

Deliver: rename “Direct roles” to “Direct assignments”; remove standalone checkbox; reuse FilterToolbar, FilterField, SearchInput, ActionButton, Status and token styles; introduce server-side search / account status / direct-grant filtering and paging, and wire results/clear chips. Preserve existing authorization and user→Grants handoff.

Acceptance: an active user with only group grants appears under “Without direct assignments” **without** being labelled waiting/denied; inactive users follow the independent account filter; search finds users beyond the first 100; filters operate before page bounds; no stale result on revoke; tablet/phone keyboard accessibility and empty/error states pass.

### Slice 2 — Read-only user detail and grant handoff

Deliver: selected-user detail, direct grants with role/scope, record links, and explicit not-evaluated group/access copy; use shared split layout and existing user-targeted grant APIs. Preserve selected identity when moving between Users and Grants, and existing no-self-edit/inactive-user safeguards.

Acceptance: multiple grants for one role are not accidentally counted as unique roles; 0 direct grants never claims 0 effective access; selected-user detail remains accessible after paging; history/browser-back, focus, conflicts and denied reads behave correctly.

### Slice 3 — Effective-access evidence/API feasibility probes

Before implementation, prove: (a) how Windows authenticated-user membership proof is obtained and expires, (b) whether trusted inspection of **another** user is supported and authorized, (c) complete vs partial groups/nested/large groups and outages, (d) matching SQL/pure/runtime paired-grant decisions for the same permission+asset, and (e) no unauthorized membership evidence leakage. Document unsupported cases as NotEvaluated; do not invent freshness or fallback.

Deliver: typed provider-neutral evidence boundary and **one-user, read-only, audited** access-inspection API only after probes pass.

Acceptance: exact scope, role provenance and confidence state; incomplete proof never becomes a “no access” verdict or grants permission; revoked direct/group grants, role inactivity, asset inactivity and source updates behave correctly; no unsafe cross-grant permission/scope union.

### Slice 4 — Effective-access UI, after trusted API exists

Deliver: selected-user summary with separate direct/group-derived assignment views, verification provenance/freshness and per-permission/per-asset inspection. Use progressive disclosure and shared Status/empty/error conventions. Avoid turning an assessment timestamp into a misleading “live now” claim.

Acceptance: group-only account is correctly explained when proof is complete, clearly Unknown when proof is unavailable, and no unauthorized directory membership lists are exposed; accessible keyboard and screen reader usage.

### Slice 5 — Provider extensibility and operational readiness

Validate multiple directory namespaces/opaque ID collisions, Windows SID normalization in its adapter, OIDC claim overage/incomplete group assertions, stale caches, directory outages and recoveries, nested membership policy, audit retention, explicit group scope, last-direct-admin preservation and service-execution boundary. Enable any global effective-access directory filtering **only** after separate completeness/scalability proofs.

Acceptance: adapters are replaceable without changing page UI or grant evaluator; unsupported providers are labelled unavailable, not falsely empty.

## Implementation and review guardrails

- Write each slice as a separate scoped PR based on current develop; reconcile concurrent work in Administration, Grants and shared components (including asset-label work in PRs #420/#421) before touching files.
- Preserve existing role/grant API server authority and no-self-escalation, last-admin, audit and revision checks. A display-only change must not rewrite stored identities or old audits.
- API additions must have strict generated contracts, status/error validation, authorization tests for security.manage, pagination/order tests in PostgreSQL and negative proof cases. Do not use frontend filtering over 100 users as a substitute.
- Follow docs/design/ui-conventions.md: shared FilterToolbar, no duplicated components, design tokens, text through t(), shared split panes and focus rules. Validate CSS coverage, locale strings, accessibility (axe), keyboard, widths 320/390/736/1440 and no overflow/nested scrolling.
- Check source identity names with anonymized fixtures, multiple users with the same display name, group-only access, no direct access, inactive user, failed provider proof, partial memberships and conflicting/retired grants.
- Full PR CI (conventions, frontend typecheck/lint/tests/build, .NET/server integration as applicable, PostgreSQL, release/installer) is the merge gate; real Windows/domain end-to-end verification remains a separate release gate.
- Do not merge a draft into develop or mark a phase implemented based on a design-only document.

## Open design checks before implementation

- Confirm query/cursor shape and indexing after inspecting the protected security directory repository and its current ordering; retain stable user IDs and avoid breaking existing consumers.
- Determine whether group-derived evidence can be verified for *arbitrary* directory users or only the current signed-in user. The latter is acceptable for V1 if clearly shown as NotEvaluated.
- Confirm an acceptable privacy/auditing contract for privileged access diagnostics; do not copy raw tokens, secret group directories or credentials into logs.
- Decide whether the selected user's read-only details should be deep-linkable as #access/users/<id> within the existing access routing, and specify graceful focus/back navigation without disrupting #access/activity/<id>.
- Review terminology with actual administrator examples (direct vs group-only vs unknown); make “Assigned”, “Verified” and “Unknown” unambiguous before implementation.

## References

Repository:
- [UI conventions](ui-conventions.md)
- [Enterprise entitlement architecture](../adr/ADR-006-enterprise-entitlements.md)
- [Grant initialization and waiting-for-access notes](../development/grant-runtime-initialization.md)
- [Scoped authorization cutover checks](../security/scoped-authorization-cutover-checklist.md)
- [PR #137 — waiting-for-access directory filter](https://github.com/abhee15/WellSiteAutoPilot/pull/137)
- [PR #313 — provider-neutral group grants](https://github.com/abhee15/WellSiteAutoPilot/pull/313)

External research:
- [PatternFly: filtering and applied-filter controls](https://www.patternfly.org/patterns/filters/design-guidelines/)
- [PatternFly: responsive toolbar design](https://www.patternfly.org/components/toolbar/design-guidelines/)
- [Microsoft identity platform: group claims limits and overage](https://learn.microsoft.com/security/zero-trust/develop/configure-tokens-group-claims-app-roles)
