# Authenticated external identity boundary

The configured ASP.NET Core authentication handler authenticates the primary
principal before `IAuthenticatedPrincipalResolver` runs. The resolver is a host
adapter, not an identity provider or a permission evaluator. Only Windows
Negotiate and the existing Development/CI TestHeader mode are supported.

`AuthenticatedExternalIdentity` separates provider namespace and external subject
from the internal WSA user GUID. It carries the persisted key and display details
needed by existing user provisioning. General access-claims middleware no longer
interprets Windows SID or provider-specific display claims.

## Stable existing links

- A canonical primary Windows SID continues to use `SID:<normalized SID>`.
  Renaming the account retains the same internal user, roles and Asset scopes.
- Without a primary SID, the existing `NAME:<normalized account name>` key is
  retained, including case-insensitive name matching.
- Secondary identities cannot supply the primary name, SID or display name.
  Ambiguous primary names/SIDs and malformed identities are denied before any
  provisioning or audit write. Product roles and permissions are not read by
  the resolver.
- SID appearance or change is a different persisted key. It does not copy
  assignments from an account with the same display name. No automatic linking,
  ownership transfer, recovery or privilege migration is implemented here.
- The existing bootstrap operation remains one-time and transactionally guarded
  by the presence of an active administrator. Inactive accounts remain denied.
- The CI provider preserves its existing local test keys; it is not supported
  alongside Windows in one production deployment. A future additional provider
  requires namespaced persistent mappings and explicit linking/recovery design.

## SID validation

The cross-platform validator accepts revision 1, a decimal identifier authority
below 2^32 or a 12-digit hexadecimal authority for larger 48-bit values, and
1–15 unsigned 32-bit decimal subauthorities. Decimal components have no leading
zeroes. Existing key case normalization and outer whitespace trimming remain.
It performs no directory lookup or remote resolution. It follows Microsoft's
[SID string syntax](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-dtyp/c92a27b1-c772-4fa7-a432-15df5f1b66a1)
and [SID packet limits](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-dtyp/f992ad60-0fe4-4b87-9fed-beb478836861).

## Validation boundaries

Foundation CI executes positive, malformed, ambiguous, secondary-identity and
unsupported-provider cases. On Windows it also constructs and round-trips the
accepted SID boundary cases with the native `SecurityIdentifier` API.

PostgreSQL CI invokes the actual access-claims middleware with trusted host test
principals and real repositories. It verifies retained SID/name account links,
account rename, SID appearance/change, no inherited grants, bootstrap protection,
inactive-user denial, provisioning audit identity, and no writes for invalid SIDs.
The existing HTTP suite continues to test supported TestHeader authentication,
401/403 boundaries and operational authorization.

These checks do not claim a real Windows browser/Kerberos/NTLM handshake.
Follow [the separate Windows Negotiate validation path](../development/frontend-authentication.md)
for field-release evidence. No database migration, runtime topology, user-facing
API, supported external provider or live authorization model changes here.
