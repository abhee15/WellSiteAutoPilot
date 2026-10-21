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
definition in the bootstrapper carries its own account; re-running the installer resets the Server,
NATS and worker to the table above.

### The Integration Gateway's account

The Gateway's Windows account is the identity ForeSite and CygNet see when it calls them, and
`LocalService` is anonymous on the network, so a real provider connection needs a domain account or,
better, a group-managed service account (gMSA). Choose it at install time; the choice is remembered, so
an upgrade that does not mention it keeps it.

| Choice | Silent install | Bootstrapper |
| --- | --- | --- |
| A group-managed service account (name ends in `$`; Windows holds the password) | `/GATEWAYACCOUNT=CORP\svc-wsa-gateway$` | `--gateway-account` |
| An ordinary domain or local account (`.\name`) | `/GATEWAYACCOUNT=CORP\svc-wsa` with `/GATEWAYACCOUNTPASSWORD=` or `WSA_GATEWAY_ACCOUNT_PASSWORD` in the environment | `--gateway-account` (password in `WSA_GATEWAY_ACCOUNT_PASSWORD`) |
| Back to `LocalService` | `/GATEWAYACCOUNTDEFAULT=yes` | `--gateway-account-default` |

The installer lets the account log on as a service (local security policy; a domain policy that sets
that right replaces it), starts the Gateway under it, and makes the Gateway's key file readable by it
and by no other service. An ordinary account's password is never stored, so an upgrade needs it again
(the installer says so); a gMSA has none to supply. The account is remembered only after the Gateway
has started under it. Connection settings can never select the account, and a connection's test
result states which account the Gateway used.

### Changing the Gateway's account later

Re-run the installer, or the bootstrapper's `install-services --install-root <path>`, with the new choice from the table
above (for an ordinary account, put its password in `WSA_GATEWAY_ACCOUNT_PASSWORD`). Every service is recreated and
restarted, so the platform is briefly unavailable. Do not change the account on the service's Log On tab in Services: that
does not grant the log-on-as-a-service right, does not give the new account the Gateway's key file, and the next
install or upgrade puts the remembered account back. Starting, stopping and restarting services from Services is fine.

### Restart on failure

Each service is set to restart itself if it stops unexpectedly (after 5, 15 and then 60 seconds; the count
starts again after a day), including when it exits with an error. Stopping a service from Services (or `sc stop`)
is a normal stop and is not restarted. Re-running the installer sets this up again.

## HTTPS

The installed Server serves **HTTPS only**; there is no plain HTTP option. (Development runs keep plain
HTTP on 5080.) The installer's "HTTPS Certificate" page, or the silent options below, choose the certificate:

| Source | Silent install | Bootstrapper |
|---|---|---|
| A certificate already in the Local Computer "Personal" store (recommended; use one from your organisation's authority) | `/CERTTHUMBPRINT=<40 hex characters>` | `--cert-thumbprint` |
| A PFX file, imported into that store | `/CERTPFX=<path>` with `/CERTPFXPASSWORD=` or `WSA_CERT_PFX_PASSWORD` in the environment | `--cert-pfx` (password in `WSA_CERT_PFX_PASSWORD`) |
| A generated self-signed certificate (trial only: browsers warn until it is trusted) | `/SELFSIGNED=yes` | `--cert-self-signed` |

Whichever you choose, the installer verifies the certificate (private key present, in date, valid for
server authentication) and gives the Server's account (`NetworkService`) read access to that one private
key. Only the thumbprint is written to the settings file (`Tls:Thumbprint`); nothing secret is stored there.
The choice is remembered: an upgrade that names no certificate keeps it, and a first silent install that
names none generates a self-signed certificate. The Server refuses to start if the certificate or its key
is missing, and says why. Changing the certificate later means re-running the installer, or the bootstrapper's
`install-services` with a new option. A certificate must be renewed (replaced) before it expires; the
installer warns when it is within 30 days. Uninstalling leaves certificates in the store.

Clients on the machine or network must use a name the certificate covers (normally the computer name).
The Integration Gateway and worker still talk to each other over loopback HTTP; they are not exposed.

## Network access

By default the Server is reachable from other computers: it listens on every interface (port 5443 unless you choose another), and the
bootstrapper adds one inbound Windows Firewall rule bound to the Server executable for **Domain and Private
networks only** (never Public). NATS (4222), the Integration Gateway (5081) and the worker (5082) stay on
loopback. Choose "this computer only" in the installer, or `/NETWORKACCESS=no`, to listen on loopback
and remove the rule. The choice is remembered, so an upgrade that does not mention it never widens or
narrows access. Uninstalling removes the rule.

**Port.** The installer has a "Server port" field, and a silent install takes `/PORT=<1-65535>` (the
bootstrapper takes `--port`). Use it when another program already holds 5443. The firewall rule and the
listening address follow the port. The port is remembered like the network choice, so an upgrade that does
not mention it keeps the earlier port. 5081, 5082 and 4222 are refused because other services use them.

Users browse to `https://<computer name>:<port>/` (port 5443 by default). Because the Server runs as `NetworkService` (the machine
account), Kerberos works for remote users without registering anything extra. For automatic sign-in the
browser must treat the address as an intranet site: Edge and Chrome do this for single-label names and for
sites listed in the `AuthServerAllowlist` policy. Otherwise they ask for credentials.

Traffic is encrypted: see HTTPS below.

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
