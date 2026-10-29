# Grant runtime development initialization

The grant runtime introduced by PR #127 binds identity-only provisioning,
explicit bootstrap and paired-grant authorization. Identity provisioning alone
does not grant access; the allowlist and one-time initialization rules below
remain mandatory.

## Fresh deployment

The configured bootstrap account must authenticate through the selected host
provider and match `Security:BootstrapAdministrators`. The grant bootstrap
repository accepts the already resolved, active persisted user; it never reads
a role, group or user ID from a request body/header. The host must check the
allowlist before calling it. TestHeader remains simulated authentication and
does not establish Windows group authority or satisfy release gate #92.

On a fresh Windows installation the bootstrapper asks Windows to resolve the
`--bootstrap-admin` account and refuses an account that does not exist, so a typo
fails at install time instead of at first sign-in. If the directory cannot be
reached (for example an offline install) it warns and continues. Disposable test
installs that use a fake account set `WSA_ALLOW_UNRESOLVED_BOOTSTRAP_ADMIN=1`;
the CI installer smoke does this. Upgrades never re-check or replace the account.

Initialization creates two direct built-in Admin assignments in one transaction:

- `DeploymentWide` for deployment permissions and direct recovery protection.
- `AllAssets` for resource permissions, including future active Assets.

One assignment does not imply the other. There are no Asset bindings on either
grant. The protected active system role must contain the complete supported
Admin permission set. The grant pair and one bootstrap audit commit atomically.
An advisory transaction lock serializes simultaneous bootstrap attempts across
server instances. No roles or Asset wildcard claims are granted implicitly.

Any existing grant, including a retired grant, prevents initialization. The
bootstrap audit also prevents reinitialization after grants are removed outside
the supported API. Retiring access or deactivating a user must never silently
regrant authority to an allowlisted account. Recovery uses the protected direct
administrator and customer-owned Windows credentials; bootstrap is not a
recovery endpoint or default account.

## Disposable development reset

Per ADR-002/ADR-006, an unreleased development database containing legacy access
data can be explicitly replaced at the runtime cutover. Stop the local server,
workers and scheduler first. Use only a disposable development database; this
procedure discards its Assets, configuration, executions, assignments and audit.

For a local PostgreSQL development instance with the usual `PGHOST`, `PGPORT`
and `PGUSER` settings and a disposable database named `wsa_dev`:

```bash
dropdb --if-exists wsa_dev
createdb wsa_dev
export WSA_DATABASE_CONNECTION_STRING='Host=127.0.0.1;Database=wsa_dev;Username=wsa'
dotnet run --project tools/WellSiteAutoPilot.DatabaseMigrator --configuration Release
```

Configure the server's `ConnectionStrings:WellSiteAutoPilot` to the same database
using local configuration/secret handling. Configure the intended Windows
bootstrap identity explicitly. After the completed cutover, authenticate that
identity once, verify its separate direct recovery/AllAssets assignments and
audit, then create other user/group assignments through Administration. Each
identity starts without access unless explicitly covered by a usable grant.

The normal server does not reset or migrate the database. Installer/migrator
credentials may be separate from runtime credentials. At the release baseline,
customer upgrades require data preservation and the recovery controls in
ADR-002; this development reset is not a customer upgrade process.

## Granting access to people who are waiting

A person who signs in without any grant sees "Waiting for access" and appears in
Administration → User assignments. That tab shows each user's number of direct
roles, and "Only users with no direct role" asks the server for active users that
have no active grant to an active role, most recently seen first, so a waiting
person is not hidden behind the 100-row page. **Assign role** opens Scoped grants
with that person already selected; the usual review, audit and last-administrator
protections still apply.

The count covers roles assigned to the person directly. Access that arrives through
a mapped Windows group is proven from the sign-in token on each request, so the
directory cannot show it: someone covered only by a group can legitimately show
"None". Default access for members of a directory group is configured under Scoped grants
by selecting Directory group, searching its name and assigning a role/scope.
Previously assigned groups can also be inspected when directory search is unavailable.
