# Slice 3: effective-access inspection evidence and feasibility

**Status:** Implemented the first, pure evidence-classification probe and tests. **No privileged inspection endpoint, UI effective-access verdict, extra directory read, new identity provider, or changed authorization rule is delivered by this PR.** The later API remains gated by the targeted feasibility findings below.

Related: [agreed Users UX plan](../design/user-access-directory-ux-plan.md), [directory group runtime](windows-group-membership.md), [identity boundary](authenticated-identity-boundary.md), [ADR-006](../adr/ADR-006-enterprise-entitlements.md).

## What is already proved in this repository

1. **Signed-in Windows user (request-bound):** `GrantIdentityMiddleware` binds the authenticated principal to the persisted local user. `RequestAccessContext` invokes `WindowsTokenGroupResolver`; in Negotiate mode it accepts the primary authenticated native `WindowsIdentity` only, matches the primary SID to the local identity, checks enabled groups with `WindowsPrincipal.IsInRole(SID)`, rejects excessive groups, and supplies `VerifiedWindowsGroupMembership` tagged `windows-ad`. Arbitrary claims, secondary identities and CI TestHeader cannot supply Windows group authority.
2. **Freshness is not token renewal:** `VerifiedGroupMembership.IsUsableFor` bounds **time since observation** to one minute. The observation timestamp does **not** say when Windows issued or renewed its access token, when Active Directory was queried, or how quickly a recently removed group disappears from the token. A bound token snapshot can be used by the existing runtime evaluator but is not a guarantee of current directory membership.
3. **Other selected user:** The administrator's token belongs to the administrator, never to the person selected in Administration > Users. The configured `IGroupDirectory` and `WindowsGroupDirectory` can search/resolve **group identity and names** but do not inspect per-user group membership. There is currently no trusted provider capable of verifying another persisted user's group access. For this use case the correct state is **NotEvaluated** (not “No access”).
4. **Authorization engine already exists:** `ScopedGrantEvaluator`, `ScopedAccessQueries` and the runtime grant repositories evaluate active roles, grants, namespace-qualified enabled groups and exact asset scope **within one grant**. They reject mismatched subject, stale/missing proof, unsupported permissions, inactive user/role/asset and invalid scopes. There is no reason to implement a second evaluator.
5. **Existing inspection is not this feature:** `ScopedAccessInspectionService` and `ScopedAccessComparisonEvaluator` implement a **diagnostic legacy-vs-proposed direct-grant comparison**, and the comparison explicitly excludes group proof. Its `ProposedAllowed=false` must never be presented as a complete effective-access denial for a group-enabled user. The service's protected administrative permission, audit, and SQL parity patterns are useful references, but its result contract is not an effective-access contract.
6. **UI is intentionally truthful:** The merged selected-user panel shows configured direct grants, with “Group-derived access: Not evaluated.” A zero direct-grant count is **not** a denial. No arbitrary-user membership assertion is needed to ship Slices 1 and 2.

## New provider-neutral probe boundary

`MembershipInspectionEvidence.ForSelectedUser(boundPrincipal, selectedUserId, nowUtc)` returns **metadata only**:

| State | Meaning | Can imply “no group-derived access”? |
| --- | --- | --- |
| `NotEvaluated` | Selected user is **not** the authenticated request principal | Never |
| `Unavailable` | Bound current user has no trusted membership evidence | Never |
| `Stale` | Bound current user's previously observed proof is outside its usable window | Never |
| `BoundRequestSnapshot` | A currently usable, provider-tagged group snapshot belongs to **this** request principal | No — proves neither renewed token nor live directory state |

Returned metadata contains no group IDs, credentials, raw token, directory topology, or role permission union. The contract is *diagnostic-only*: it does not change `AccessSubject`, grant SQL predicates, revocation logic, policy handlers or physical-control authorization. It is not a general provider-agnostic guarantee of complete identity-provider evidence (an Entra/OIDC adapter must explicitly handle group-claim overage and proof completeness).

Regression checks in `MembershipInspectionEvidenceChecks` cover own snapshot, unrelated user, missing proof, wrong namespace/case, exactly-at-expiration, stale/future observation, direct vs group paired grants, and invalid selected-user IDs. Existing `WindowsGroupGrantChecks`, `DirectoryNamespaceChecks`, `WindowsGroupResolverChecks`, `GroupOwnAccessPersistenceChecks` and `ScopedAccessInspectionChecks` remain the broader baseline.

## Targeted feasibility probes before a privileged effective-access API

### A. Windows / AD native-token test — needs real deployment evidence

- A first-time user with a direct and a nested security-group grant; verify Negotiate on the actual configured host, AD forest/trust conditions, group SID namespacing, and observed group availability at first sign-in.
- A group removal while the browser keeps the same Windows session: measure when a newly authenticated/reissued token drops the grant. Do not call a one-minute observation TTL a revocation SLA.
- Deny-only/disabled token SIDs, UAC filtered tokens, very large group sets (including >2048 groups), cross-domain groups, renamed/deleted group, and directory outage. Confirm no incomplete/token-less response becomes evidence of absence.
- Existing CI native-token test runs on a CI service identity; it does **not** prove browser/AD/Kerberos/NTLM behavior. This is an explicit owner/environment acceptance gate.

### B. Another-user membership — **unsupported in V1 until proven**

Decide whether a **separate read-only privileged provider** can obtain a user's group information without impersonation and without confusing directory transitive membership with an enabled Windows *access token*. In AD, LDAP computed `tokenGroups` can provide transitive SIDs but depends on Global Catalog availability and has substantial domain-controller cost. It is **not** a copy of a live authenticated Windows token's enabled/deny-only group flags. Its permissions, trust/domain-local coverage, completeness, paging/bounds, caching, user SID mapping and freshness need independent validation.

A future Entra/OIDC provider has different identifiers (tenant + object ID) and group-claim overage behavior; Microsoft documents token group limits and Graph transitive-membership APIs with separate permissions for querying another user. Do not treat a missing `groups` claim as proof of no groups.

If this probe cannot deliver complete and authorized proof for a particular user, the future API must return `NotEvaluated` or `Unavailable`, **not** `VerifiedNoAccess`. Never fall back to the administrator's membership.

### C. Read-only, protected access-inspection design

Once A and B demonstrate an acceptable provider capability for the intended deployment:

1. Define a separate provider capability for `InspectMembershipForUserAsync(localUserId, ...)` returning a typed, bounded response with `DirectoryId`, provenance, completeness/unknown reason, observed time and proof expiry. Distinguish native-login request evidence from directory-retrieved membership; do not invent a token for another user.
2. Require **one selected local user, one known permission, one optional explicit asset ID**, and enforce scope kind, active user/asset, and current protected `security.manage` authorization **before and after** any slow provider lookup.
3. Evaluate using the **same** `ScopedGrantEvaluator` and SQL grant-pairing semantics. Show an affirmative “allowed” only with a usable matching grant; show “verified no access” only when relevant evidence is demonstrably complete for the requested context and security state is fresh. Incomplete/expired/unavailable evidence leads to an **unknown** verdict rather than a false denial.
4. Persist a minimal, durable `security.effective-access.inspected` audit with actor, target, scope, correlation and outcome/evidence state **before** disclosing the result, under the same admin revocation fence as the existing diagnostic. No raw group IDs, token, password, or private directory dumps in audit logs.
5. Add PostgreSQL integration parity tests for direct, group-only, combined, revoked grants and roles, disabled account/assets, namespace collision, unknown proof, unauthorized actor and provider error. Include race/revocation, audit failure and concurrent grant-write checks. Never authorize production requests using a diagnostic result.
6. Add selected-user UI progressive disclosure only after the protected API passes. The current “Not evaluated” message is correct until then. Do not create a whole-directory “users without access” filter from per-user token snapshots.

## Outcome / decision

**Proceed with the safe evidence boundary and probes now. Do not begin a cross-user group membership API solely by reusing existing group-name search or direct-only legacy inspection.** The next concrete engineering step is an opt-in, environment-bounded Windows domain probe with a least-privilege read evaluation for a selected user. If it cannot produce equivalent semantics, keep group-derived access “Not evaluated” and scope the next inspector to explicit **direct** permissions only, without claiming completeness.

## Microsoft source references

- [Windows security-group access token SID flags, including enabled and deny-only](https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-token_groups)
- [AD tokenGroups computed attribute and Global Catalog / cost](https://learn.microsoft.com/windows/win32/adschema/a-tokengroups)
- [AD tokenGroupsGlobalAndUniversal is not all domain-local membership](https://learn.microsoft.com/openspecs/windows_protocols/ms-adts/a366b6b6-f36f-46e8-ba2d-af79bf8966bf)
- [Microsoft Entra group-claim limits and overage](https://learn.microsoft.com/entra/identity/hybrid/connect/how-to-connect-fed-group-claims)
- [Microsoft Graph transitive memberships and permission differences](https://learn.microsoft.com/graph/api/user-list-transitivememberof)

## Follow-up probe: opt-in selected-user AD snapshot (separate PR)

A dedicated **standalone Windows PowerShell diagnostic** is now available in
`tools/ci/windows-domain/Invoke-SelectedUserMembershipProbe.ps1`, with execution
and acceptance guidance in
[Windows domain membership probe](../testing/windows-domain-membership-probe.md).

It deliberately has **no Web API surface, service registration, database write,
identity impersonation or runtime grant-evaluator integration**. Operators must opt
in to a single user-SID and expected-group-SID read under the machine's joined
domain and current process's existing integrated permissions. The diagnostic
reports a sanitized **AD transitive SID snapshot**, its bounded duration and
whether that SID appeared, optionally comparing the current process's own enabled
token group only when the process user SID matches the selected account.

A new **Windows CI self-test** validates LDAP binary SID escaping and the
domain-free path. This is not a real-domain test; field validation is still
required. `tokenGroups` may be expensive, depends on a GC, and is not a
substitute for token `SE_GROUP_ENABLED`/deny-only semantics. **All negative
or partial results remain unknown rather than "verified no access."**

**Decision:** a future selected-user administration API must **not** convert
these computed directory SIDs to `VerifiedGroupMembership` or treat the
snapshot as permission to run a Logic Module or control an Asset. Complete
provider-specific semantics and authorization/audit design must be separately
proved. Current UI copy "Not evaluated" remains correct.
