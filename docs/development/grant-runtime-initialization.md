# Grant runtime development initialization

Implementation status: part of draft PR #127. The host still uses legacy
authority until the complete runtime replacement is integrated and validated.
Do not interpret the bootstrap repository's presence as runtime activation.

## Fresh deployment

The configured bootstrap account must authenticate through the selected host
provider and match `Security:BootstrapAdministrators`. The grant bootstrap
repository accepts the already resolved, active persisted user; it never reads
a role, group or user ID from a request body/header. The host must check the
allowlist before calling it. TestHeader remains simulated authentication and
does not establish Windows group authority or satisfy release gate #92.

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
