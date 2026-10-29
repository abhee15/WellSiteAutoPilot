# Directory groups and Windows membership

## Access model

WSA assigns configurable roles and Asset/deployment scopes to local users or directory
groups. A group's stable identity is `(DirectoryId, GroupId)`, not its name. Opaque IDs
remain case-sensitive in both the evaluator and explicitly ordinal database columns; the Windows adapter alone validates and canonicalizes SIDs.
The generic grant evaluator, SQL predicates, own-access guard and persisted execution
read authority all preserve the directory namespace. Roles and scopes remain paired
inside each grant. Windows and a future trusted identity integration use the same
WSA authorization model.

## Administration

Administration → Scoped grants → Directory group offers name-prefix search and
readable domain-qualified results. The server resolves the selected ID again before
creating a grant and stores provider-resolved name/domain metadata. Administrators do
not enter SIDs. Previously assigned groups remain selectable without a reachable
external directory, including retired assignments and unresolved legacy display names.
This allows inspection and retirement after directory outage or deletion; creating
new assignments requires a successful directory resolution. The saved-group list is
bounded at 500; it fails explicitly rather than silently truncating.

The first directory implementation searches AD security-group account names in the
Server's joined domain under the Server service identity. It uses the existing
AccountManagement dependency, a bounded prefix query, 50-result pages with a refine
hint, directory timeouts and one outstanding native lookup per Server instance.
Cancellation stops the HTTP caller's wait; native calls retain the concurrency slot
until they really finish. Directory error text is never exposed. No supplied LDAP
endpoint, impersonation, administrator password or non-security-group fallback exists.
Member enumeration is not required for inheritance and is not implemented.

`Security:DirectoryProvider` accepts `active-directory` or `none`, independently of
`Security:AuthenticationMode`. If omitted, Negotiate defaults to AD and TestHeader to
none. Configuring a directory does not enable an authentication provider. Future
providers need a trusted identity/membership adapter and, independently, a directory
adapter if name search is desired; they do not need another role/scope evaluator.

Administrative routes under `/api/v1`:

- `GET /security/directory` — directory search capability.
- `GET /security/directory/groups?prefix=Software` — live metadata search.
- `GET /security/groups` — previously assigned group identities and saved labels.
- `GET/POST /security/directories/{directoryId}/group-grants?groupId=...`.
- `PUT /security/directories/{directoryId}/group-grants/{grantId}/deactivate?groupId=...`.

All require SecurityManage and current protected administrator authority. Directory
lookup checks authority again before returning results. Existing grant mutation
transactions retain audit atomicity, revision checks, same-role/scope uniqueness,
own-access protection and the protected direct recovery administrator.

## Request-time membership and first sign-in

GrantIdentityMiddleware persists the authenticated local identity, then
RequestAccessContext obtains evidence from the configured trusted host adapter.
Identity creation grants no authority by itself. A first-time Windows user whose
enabled token contains an assigned group receives that group's usable grants without
an individual assignment. Users covered by no usable grant remain waiting for access;
inactive local users remain denied.

WindowsTokenGroupResolver accepts only the primary authenticated native WindowsIdentity
under Negotiate, checks its user SID against the resolved local identity and retains
only groups for which WindowsPrincipal.IsInRole(SID) is true. Disabled/deny-only groups,
generic claims, secondary identities, browser input and HTTP test headers cannot
supply membership. TestHeader is explicitly denied group authority even if a resolver
is accidentally injected. A future genuine provider can supply directory-qualified
VerifiedGroupMembership from its own validated issuer/tenant/session; merely copying
claims from an arbitrary principal is insufficient.

Evidence is immutable, bound to one local user and usable for at most one minute from
observation. SQL checks current user/grant/role/Asset state on each query. WSA does not
cache effective permissions per user. The observation bound does not renew a Windows
token or prove that AD was queried. Nested membership and AD removal take effect when
Windows supplies a renewed token; validate actual browser/domain renewal behavior in
the target deployment before claiming a revocation latency.

User administration counts direct assignments only. A zero count does not mean a
person lacks group-derived access. Directory listings do not prove another person's
current token membership.

## Schema and validation

The directory-qualified-grant migration renames GroupSid to GroupId, maps existing
WindowsGroup rows to Group/windows-ad, adds saved display metadata and makes active
assignment uniqueness directory-qualified. No reset or automatic startup migration is
introduced. The migrator/installer remains responsible for migrations. Downgrading
with non-Windows assignments is refused to prevent losing their namespace.
Old in-flight execution read authorities without a directory namespace cannot regain
group authority; request a new execution rather than inferring a namespace.

Automated checks cover paired direct/multiple-group isolation, namespace and case
isolation, proof freshness/identity binding, inactive users, own-access protection,
metadata search authorization, unsupported directories and readable picker behavior.
PostgreSQL checks cover saved labels, directory-qualified uniqueness, first-time
identity inheritance and namespace filtering. Windows CI token checks are not browser
Negotiate or live AD proof. Live service-account lookup, domain/nested membership,
first-time sign-in, group rename/deletion and actual token-renewal behavior remain
owner acceptance checks.
