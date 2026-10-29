# Directory group access

## Goal and approved boundary

Administrators find SoftwareEngineers by name, select its directory-qualified identity,
assign a configurable WSA role and scope, and members receive that assignment on their
first Windows sign-in. Administrators do not obtain SIDs manually. The owner approved
planning and implementation with provider neutrality as a requirement.

Authentication, directory discovery and authorization are separate responsibilities.
Directory lookup is metadata, never membership proof. Roles and Asset scopes remain
paired in one assignment, with current transactional revocation/recovery protections.

## Repository findings

- GrantIdentityMiddleware provisions identity and initializes RequestAccessContext;
  the latter binds native enabled Windows-token membership to the persisted user.
  The old statement that group grants are proposed-only in
  docs/security/windows-group-membership.md is stale; do not add a second runtime.
- AccessGrantSubjectKind.WindowsGroup, GroupSid, VerifiedWindowsGroupMembership,
  evaluator/SQL predicates, APIs and UI are Windows-specific today. A neutral search
  interface alone does not make the existing authorization model provider-neutral.
- GrantAdministrationPage asks for a SID and cannot search or resolve a group name.
- SecurityDirectoryRepository counts direct assignments only. That is not an
  effective-access count; group members must not be labelled as having no access.
- DirectoryDisplayNameResolver already uses AccountManagement for presentation.
  Reuse the installed package; do not reuse its per-user metadata cache for permissions.

## Delivery slices

### 1. Administrative directory lookup

IGroupDirectory returns (DirectoryId, ExternalId), display name and domain. The
application service checks current administrator authority before lookup and again
before disclosure. The first adapter searches security groups by account-name prefix
in the server's joined AD domain, under its service identity. Bounded results,
timeouts and concurrent operations; no administrator-supplied endpoints/credentials,
impersonation, wildcard query or membership fallback. Unsupported providers report
unsupported instead of pretending the directory is empty.

GET /api/v1/security/directory/groups?prefix=Software uses SecurityManage and the
existing administrator authority. This read-only API does not create grants.
Current composition registers AD only for Negotiate; future configuration must select
the authentication integration and directory independently. That composition choice
is not encoded in the application interface.

### 2. Directory-qualified assignments and membership

Before connecting search selection to grant writes, replace the Windows-only external
subject representation with a directory-qualified group key. Keep local WSA user IDs
for direct assignments. Identity must include the directory/tenant namespace, not a
provider display name. AD uses its stable SID; another directory may use case-sensitive
opaque IDs. Never globally uppercase arbitrary provider IDs. A second provider may
not borrow an AD key even if its display name or external ID matches.

Replace Windows-specific evidence in the shared evaluator with immutable request-bound
membership evidence tied to the resolved local user and directory namespace. Windows
validation stays in the Windows adapter, including enabled-token checks and matching
native user SID. Unsupported authentication supplies no group evidence. No request
body/header can supply proof. Preserve freshness bounds, inactive-user denial,
role/scope pairing, own-access restrictions and direct recovery-admin protection in
both pure and SQL evaluation.

Update schema, EF configuration/migration, API contracts/generator, audit and fixtures
together. Follow the disposable-development database policy; no automatic reset.
Preserve current SID assignments by an explicit mapping decision in that change, or
document an explicit development reseed if the schema requires it. Do not reinterpret
existing SID rows as arbitrary provider subjects.

### 3. Group administration experience

Add a capability-aware group picker, domain-qualified search results and selected
subject summary to existing role/scope authoring. Persist server-resolved display
metadata (no client name as authority), show readable subjects in audits and existing
assignments, and retain unresolved/deleted-group assignments for inspection/retirement.
Renaming metadata must not retarget a grant. Selection is not authorization proof.
A recreated group with the same name and a new ID does not inherit access.

Keep user/group assignment and role/scope terminology understandable. Member browsing
is optional and separate from assignment; it must not become bulk per-user provisioning.
Show direct-role counts accurately, and describe inherited access/provenance without
claiming administrator directory listings prove another user's current token membership.

### 4. Acceptance and documentation

Automated coverage: non-admin refusal before lookup, authority revoked during lookup,
unsupported/unreachable directories, literal prefix validation, cancellation and bounded
results; duplicate names in separate namespaces; opaque case-sensitive IDs; mixed direct
and multiple-group grants; role/Asset isolation; disabled user; revoked grant; first-time
identity provisioning with an existing group grant; no grant means waiting for access.

Windows/AD owner validation: service account directory reads, a real domain group,
first-time member, nested security membership, duplicate names, deleted/renamed groups,
directory outage, and observed membership removal after actual token renewal. Local
process-token tests and CI headers do not establish browser/domain behavior.

Update windows-group-membership.md after runtime verification; remove stale proposed-only
language without implying new domain testing. Keep an explicit validation matrix.

## Future-provider walkthrough (design check, not an implementation commitment)

An Entra/OIDC adapter would validate issuer/tenant/token first and supply group object
IDs in that namespace, accounting for incomplete/overage membership evidence. A separate
directory integration could search names if configured with read permission; absence of
search must not prevent authentication. The same WSA group key, role/scope assignment,
evaluator and first-time-user behavior must work without WindowsSidFormat or LDAP.
Do not implement this by accepting arbitrary group claims or by identifying users by email.

## Research references

- Owner-supplied ForeSite review: ADGroupRoleResolver.cs and AdministrationService.cs
  search/select name, store SID, and resolve access through groups. Source was not
  directly available here; do not claim ForeSite code was independently inspected.
- https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.accountmanagement.principalsearcher
- https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.accountmanagement.principalsearcher.queryfilter
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/claims
- https://github.com/Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2/tree/master/5-WebApp-AuthZ/5-2-Groups

## Original first-slice checkpoint

The original branch checkpoint started slice 1; it does not complete the picker, neutral grant schema,
member browsing or live AD acceptance. Local environment has no .NET SDK or AD.
Conventions and API authorization inventory can run locally; compilation and new .NET
regressions require CI. Keep the PR draft. No migration or installer changes in slice 1.

## Implementation update

The branch now includes the directory-qualified grant/membership representation, SQL
namespace predicates and uniqueness, a preserving migration, saved display metadata,
server re-resolution at grant creation, capability-aware name picker, previously
assigned groups for offline inspection/retirement, and a non-Windows trusted-adapter
walkthrough test. The shared authorization engine does not require Windows SIDs.
DirectoryProvider is configurable independently from AuthenticationMode.

Member browsing remains optional follow-up. Supporting a new authentication provider
requires its own verified identity/membership adapter and configuration, not a new
role or scope model. This change does not enable OIDC authentication.

The raw-SID administration endpoints are replaced as part of the unreleased v1 API;
regenerate clients from OpenAPI. Existing data is preserved by the explicit Windows
namespace mapping, and old names are retained in migration history only.
Local SDK installation now permits compilation and pure/query-translation tests;
CI remains the source of installed-Windows and PostgreSQL integration evidence.
