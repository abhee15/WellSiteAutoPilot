# Installer

V1 installer architecture:

```text
NSIS
  -> WellSite AutoPilot .NET Bootstrapper
      -> preflight
      -> machine/runtime configuration
      -> PostgreSQL setup/connectivity
      -> EF database migrator
      -> NATS configuration
      -> Windows service registration
      -> certificate/firewall configuration
      -> compatibility validation
      -> health validation
```

The installer pipeline will be introduced with the first runnable application skeleton so every useful development build can produce a downloadable Windows installer artifact.

## Service accounts

| Service | Account | Why |
| --- | --- | --- |
| Server | `NT AUTHORITY\NetworkService` | Authenticates browser users with Negotiate (Kerberos/NTLM) and reads the directory for display names and groups. Both need a network identity, which is the machine account. |
| NATS, Integration Gateway, .NET worker | `NT AUTHORITY\LocalService` | Loopback-only; they need no network identity. |

`LocalService` presents anonymous credentials on the network, so a Server running as `LocalService`
cannot complete a Negotiate sign-in and browsers prompt for credentials in a loop. Each service
definition in the bootstrapper carries its own account. A site that needs a managed service account
or a domain account can change a service's log-on account after installation (re-running the installer
resets it to the table above); the Server's account then needs read access to the machine settings
file described below.

## Network access

By default the Server is reachable from other computers: it listens on every interface (port 5080 unless you choose another), and the
bootstrapper adds one inbound Windows Firewall rule bound to the Server executable for **Domain and Private
networks only** (never Public). NATS (4222), the Integration Gateway (5081) and the worker (5082) stay on
loopback. Choose "this computer only" in the installer, or `/NETWORKACCESS=no`, to listen on loopback
and remove the rule. The choice is remembered, so an upgrade that does not mention it never widens or
narrows access. Uninstalling removes the rule.

**Port.** The installer has a "Server port" field, and a silent install takes `/PORT=<1-65535>` (the
bootstrapper takes `--port`). Use it when another program already holds 5080. The firewall rule and the
listening address follow the port. The port is remembered like the network choice, so an upgrade that does
not mention it keeps the earlier port. 5081, 5082 and 4222 are refused because other services use them.

Users browse to `http://<computer name>:<port>/` (port 5080 by default). Because the Server runs as `NetworkService` (the machine
account), Kerberos works for remote users without registering anything extra. For automatic sign-in the
browser must treat the address as an intranet site: Edge and Chrome do this for single-label names and for
sites listed in the `AuthServerAllowlist` policy. Otherwise they ask for credentials.

**Traffic is plain HTTP.** Negotiate protects the sign-in, not the page content or API traffic. Add TLS
(certificate on the Server) before using this across less-trusted networks; it is not provided yet.

## Database connection

The installer does not create PostgreSQL, the database, or its login. The database must already exist
and the user must be able to create tables in it. The installer then:

1. stores the connection in `%ProgramData%\Weatherford\WellSite AutoPilot\server.settings.json` under
   `ConnectionStrings:WellSiteAutoPilot`, in a file readable only by SYSTEM, Administrators (full
   control) and the Server's account (read). The file is locked down before the password is written;
2. runs the database migrator against that connection, **before any service is created or started**.
   A failed migration (wrong password, database missing, PostgreSQL stopped) stops the install with the
   migrator's message, with the password removed, and leaves existing services untouched;
3. starts the services. The Server reads the connection from that file.

An installed Server with no connection configured refuses to start and names the file to fix, instead of
failing on the first request with "no password has been provided". The passwordless development default
applies only when the Server is not running as a Windows service.

Upgrading over a running installation stops and removes the services first (they lock their files, and
an old Server must not run against a migrated schema), keeps the stored connection and bootstrap
administrator, migrates, then re-creates and starts the services. CI installs twice to prove this.

Interactive installs ask for host, port, database, user and password. On an upgrade, leave user and
password empty to keep the stored connection; supplying them replaces it (for example after a password
change).

Silent installs: set `WSA_DATABASE_USER` and `WSA_DATABASE_PASSWORD` in the environment of the installer
process (recommended, keeps the password off the command line) or pass `/DBUSER=` and `/DBPASSWORD=`.
`WSA_DATABASE_HOST` (default `127.0.0.1`), `WSA_DATABASE_PORT` (default `5432`) and `WSA_DATABASE_NAME`
(default `wellsite_autopilot`), or `/DBHOST=`, `/DBPORT=`, `/DBNAME=`, are optional. A single
`WSA_DATABASE_CONNECTION_STRING` can be supplied instead of the parts.

```text
set WSA_DATABASE_USER=wsa
set WSA_DATABASE_PASSWORD=...
WellSiteAutoPilot-Setup.exe /S /BOOTSTRAPADMIN=CORP\jsmith /DBNAME=wsa_prod
```

Known limits, to be hardened later: the password is stored in the protected settings file rather than
encrypted with DPAPI, and creating the database and login (needing a PostgreSQL administrator
credential) is still manual.
