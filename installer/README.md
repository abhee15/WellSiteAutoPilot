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
