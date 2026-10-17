# Aspire local development plan (proposed)

Status: proposed 2026-10-17, awaiting user confirmation of scope. Aspire is a
development convenience only (see the architecture baseline); production stays
NSIS plus the WellSite AutoPilot bootstrapper.

## Goal

One command starts the whole local system and lets the developer choose the
authentication provider:

- Services: PostgreSQL (plus the database migrator), the NATS host, the Server,
  the Integration Gateway, the .NET Logic Worker, the Provider Simulator, the
  Python worker, and the React frontend.
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
- A container runtime may not exist on every laptop. PostgreSQL should be either
  a container or a connection string to an existing local instance.
- Package versions (Aspire 13.x for .NET 10) are not verifiable from the
  authoring workspace; the first CI build confirms them.

## Increments

1. AppHost skeleton: PostgreSQL, migrator, NATS host, Server, Vite; TestHeader
   and Windows launch profiles; CI builds the AppHost. No runtime change.
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
