# Aspire local development plan (proposed)

Status: proposed 2026-10-17, revised the same day after user feedback; awaiting confirmation of scope. Aspire is a
development convenience only (see the architecture baseline); production stays
NSIS plus the WellSite AutoPilot bootstrapper.

## Goal

One command starts the whole local system and lets the developer choose the
authentication provider:

- Services: the database migrator, NATS, the Server, the Integration Gateway,
  the .NET Logic Worker, the Provider Simulator, the Python worker, and the React
  frontend, against the developer's **installed PostgreSQL** and a **locally
  installed nats-server**. No containers: the installed products are closer to a
  customer deployment and catch Windows-specific problems sooner.
- Provider choice: `TestHeader` or `Negotiate` (Windows) via two launch profiles
  of the AppHost, so it is a dropdown in an IDE or `--launch-profile` on the CLI.
- Both providers get the same startup order, connection strings and ports.

## What is different about the two providers

TestHeader works through the Vite dev server, which injects the test identity
for `/api`. Windows Negotiate cannot go through that proxy: the handshake is
bound to one connection, so the browser has to talk to the Server directly on the
same origin. Today that means copying `dist` into `wwwroot`. The plan removes the
copy.

## Frontend serving (the part that removes the copy step)

- TestHeader profile: keep the Vite dev server as the browser origin (HMR works,
  the existing proxy injects the identity). Unchanged security rules.
- Windows profile: the browser origin is the Server (`http://<host>:5080`).
  The Server, in the Development environment only, serves static files from a
  configured folder (`Frontend:DevelopmentStaticRoot`, default unset) instead of
  `AppContext.BaseDirectory/wwwroot`. The AppHost points it at `src/web/dist`
  and runs `vite build --watch`, so edits rebuild in place with no copy. A
  browser refresh picks them up (no HMR; a development forwarder to the Vite
  server for non-`/api` paths is a possible later upgrade).
- The setting is ignored outside Development, so production packaging is
  untouched.

## Known constraints

- Aspire proxies endpoints by default. The Server endpoint should be configured
  unproxied on a fixed port so the Negotiate handshake and the host name the
  browser trusts are predictable.
- Kerberos needs a host name and a matching service name registration. A
  user-mode `dotnet run` under the developer account will normally fall back to
  NTLM; that is acceptable for local work and does not replace the domain gate.
- Package versions (Aspire 13.x for .NET 10) are not verifiable from the
  authoring workspace; the first CI build confirms them.

## Local configuration (database and NATS)

Per-developer settings live in a gitignored `appsettings.Local.json` next to the
AppHost, with the password in user-secrets (never in the file). A committed
`appsettings.Local.example.json` documents every key.

```json
{
  "Database": {
    "Host": "localhost", "Port": 5432, "Username": "postgres",
    "Name": "wsa_dev",
    "Mode": "Reuse"
  },
  "Nats": {
    "Mode": "Start",
    "ServerPath": "C:\\tools\\nats\\nats-server.exe",
    "StoreDirectory": "C:\\wsa-dev\\jetstream"
  },
  "Auth": "TestHeader"
}
```

Database modes (selected per launch profile or by editing `Mode`):

- `Reuse` (default): connect, create the database if it does not exist, apply
  pending migrations, keep all data.
- `Reset`: drop and recreate the named database, then migrate. Start from scratch.
- `FromTemplate`: recreate the database from a saved template database
  (`CREATE DATABASE ... TEMPLATE`) for a fast return to a known seeded state.

Several named databases (`wsa_dev`, `wsa_demo`, ...) are just different `Name`
values, so switching between "existing" and "fresh" never touches the other one.
The AppHost also exposes dashboard commands (Reset database, Save as template)
that call the same code. Safety rules for anything destructive: host must be
local, the name must start with `wsa_` (the prefix the CI fixtures use), and a
`Reset` is logged with the target name. The existing shipped
`DatabaseMigrator` is reused unchanged for migrations; create/drop lives in the
AppHost only, so no shipped tool gains a destructive switch.

NATS modes: `Start` runs the developer's own `nats-server` with `-js -a 127.0.0.1
-p 4222 -sd <StoreDirectory>`; `External` assumes one is already running on 4222
and only waits for it. The repository's `NatsHost` is **not** used locally: it
looks for a bundled `..\NATS\nats-server.exe` and the ProgramData store, which
only exist in an installed layout.

## CI impact

No existing job needs to change. Today every job runs `dotnet build
WellSiteAutoPilot.slnx`, so adding the AppHost to that solution would silently
pull the Aspire SDK and its packages into all of those builds. To avoid
touching working jobs, the AppHost lives under `dev/` in its own solution file
(`WellSiteAutoPilot.Dev.slnx`) and is **not** part of `WellSiteAutoPilot.slnx`.
Because an unbuilt project rots, one new path-filtered, build-only job (runs only
when `dev/**` or its dependencies change) keeps it compiling. Installer, Windows
diagnostic and test jobs are unaffected.

## Increments

1. AppHost skeleton under `dev/`: database preparation (modes above), migrator,
   local nats-server, Server, Vite; TestHeader and Windows launch profiles; one
   new path-filtered build-only CI job. No runtime change.
2. Add Integration Gateway, .NET worker, Provider Simulator and the Python worker
   with the existing ports and `WaitFor` ordering.
3. Windows profile: `Frontend:DevelopmentStaticRoot`, unproxied fixed Server
   endpoint, `vite build --watch`; test that the Server serves the folder only in
   Development.
4. Replace the manual steps in `frontend-authentication.md`; optionally add an
   AppHost-based integration smoke in CI.

## Out of scope

Replacing the installer, running Aspire in production, and any change to what
the Server accepts as an identity.
