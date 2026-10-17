# Local development with Aspire

Developer tooling only. The installer and production deployment do not use it.

## What it starts

Your installed PostgreSQL (database prepared and migrated), your own `nats-server`,
the Provider Simulator, Integration Gateway, .NET worker, Python worker, the Server,
and the React frontend. It also builds the TypeScript API client, which is not committed.

## One-time setup

1. Install the .NET SDK from `global.json`, Node.js, and Python 3.12 or newer.
2. Copy `dev/WellSiteAutoPilot.AppHost/appsettings.Local.example.json` to
   `appsettings.Local.json` in the same folder (gitignored) and edit it: your
   database name (must start with `wsa_`), the path to `nats-server.exe`, and a
   JetStream store folder.
3. Store the database password as a secret, not in the file:
   `dotnet user-secrets set "Database:Password" "<password>" --project dev/WellSiteAutoPilot.AppHost`

## Run

- Test identity (Vite dev server, hot reload): `dotnet run --project dev/WellSiteAutoPilot.AppHost --launch-profile TestHeader`
- Windows authentication (Server serves `src/web/dist`, rebuilt on change):
  `dotnet run --project dev/WellSiteAutoPilot.AppHost --launch-profile Windows`

The Aspire dashboard opens at http://localhost:15170. If a port is already used, for
example by the installed WellSite AutoPilot services, startup stops and names it.

## Database modes (`Database:Mode`)

| Mode | Effect |
| --- | --- |
| `Reuse` (default) | Create the database if missing, apply migrations, keep data. |
| `Reset` | Drop and recreate, then migrate. Local server only. |
| `FromTemplate` | Drop and recreate from the saved template. Local server only. |
| `SaveTemplate` | Save the current database as the template, then continue. Local server only. |

Switch databases by changing the name (`wsa_dev`, `wsa_demo`); each is independent.

## Windows mode notes

Open the address by machine name, not an IP, and keep that host in your browser's
integrated-authentication allowlist. Running as your own account normally uses NTLM;
real Kerberos needs a service-name registration and is covered by the domain test gate.
