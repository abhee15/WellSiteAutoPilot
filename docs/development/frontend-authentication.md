# Frontend development authentication and Windows Negotiate validation

## Why two test paths are necessary

ASP.NET Core's Windows Negotiate authentication on Kestrel uses a multi-request,
connection-bound handshake. The Vite development proxy is **not** a Windows-auth
reverse proxy with guaranteed per-browser-client TCP affinity. A browser can
receive an initial `401 WWW-Authenticate: Negotiate`, retry with credentials,
then receive `400` if the next handshake leg reaches a different upstream
connection. A PowerShell request returning only `401` has not proven that the
full browser handshake works.

**Do not debug this by disabling authentication, accepting an anonymous API,
adding per-endpoint bypasses, or checking a proxy impersonation header into
production settings.**

## Path A — everyday React development (supported Vite flow)

The repository's opt-in Vite proxy adds one fixed `X-WSA-Test-Identity` on
`/api` requests and removes any browser-supplied identity/display headers.
This is a **development simulation**, not Windows authentication.

1. Use a disposable, local PostgreSQL database with no customer data. Run
   Kestrel only on `127.0.0.1:5080` with a separate development configuration.
   On PowerShell, in the root of the repository:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   $env:Security__AuthenticationMode = "TestHeader"
   $env:Security__BootstrapAdministrators__0 = "LOCAL\wsa-dev-operator"
   # Set your LOCAL, disposable database connection separately.
   # $env:WSA_DATABASE_CONNECTION_STRING = "Host=127.0.0.1;...;Database=wsa_dev_only"
   dotnet run --project src/server/WellSiteAutoPilot.Server/WellSiteAutoPilot.Server.csproj --urls http://127.0.0.1:5080
   ```

   The bootstrap account above is for a **local test database only**.
   For access-denied testing, choose a separate test identity that is **not**
   a bootstrap administrator. No setting in this example is appropriate for
   production, shared Windows services, real field data, or remote access.

2. Copy `src/web/.env.example` to `src/web/.env.local` and enter the same
   local test identity. `.env.local` is gitignored. Never prefix these
   variables with `VITE_` or expose the test identity through client code.

3. In a second terminal, from `src/web`:

   ```powershell
   npm install
   npm run dev
   ```

4. Open `http://127.0.0.1:5173`. Vite serves the React app; the authenticated
   API uses its built-in loopback proxy. `GET /api/v1/security/me` should
   return the local test user after the database and migrations are ready.
   A user without a given permission should still receive `403`.

The Vite configuration **fails startup without explicit auth mode** and rejects
`Negotiate` in this path. It also rejects non-loopback API origins, injects
headers only for `/api`, and removes identity assertions on `/health`.
The backend refuses TestHeader in Production and refuses TestHeader requests
from non-loopback remote addresses. Local processes can impersonate a test
account by contacting the local backend directly: **never point this setup
at a real customer or production database**.

Changing `WSA_DEV_AUTH_MODE`, the identity, or API target requires restarting
Vite. Changing backend authentication mode requires restarting the server.

## Path B — genuine Windows Integrated Authentication validation

Vite's TestHeader path **does not verify Windows/Kerberos/NTLM login,
credential freshness, browser intranet-policy settings, or reverse proxy
connection affinity**.

For a real Windows test:

1. Build the React app and deploy/copy its `dist` assets to the ASP.NET
   server's `wwwroot` (use the existing published-server/installer
   packaging path where possible). Do not route `/api` through Vite.
2. Run that server on a local Windows machine using `Negotiate`, with
   `Security__AuthenticationMode=Negotiate` and a normal Windows principal.
3. In Edge/Chrome with approved Integrated Windows Authentication policy,
   navigate **directly to the ASP.NET-hosted origin** such as
   `http://127.0.0.1:5080/`, then check `/api/v1/security/me`.
   Verify the network trace reaches a final authenticated response after the
   challenge. Confirm a real user without privileges receives `403`
   on a protected operation. Exercise session/role revocation if available.
4. Repeat against the supported field deployment topology before release.
   A development single-machine pass does not prove any future external
   reverse proxy supports Negotiate.

Where the browser or the machine lacks Windows credentials/domain policy,
record the genuine IWA test as **not executed**; do not replace it with
TestHeader and claim a pass. Native Windows/AD browser authentication is a
separate release gate from portable Vite proxy tests.

## Diagnose the failing boundary

| Symptom | Meaning / next action |
| --- | --- |
| Vite startup rejects missing `WSA_DEV_AUTH_MODE` | Opt in to TestHeader in `.env.local`; the intentional guard prevents silent broken Negotiate proxying. |
| Vite rejects non-loopback target | Use local Kestrel or follow the supported server-hosted Windows test. |
| Direct TestHeader API request without header returns `401` | Expected: there is no authenticated principal. |
| Vite API request returns `401` | Verify backend `Development`, `Security__AuthenticationMode=TestHeader`, identity and Vite restart. |
| Authenticated API returns `403` | Check active user and RBAC/Asset scope, not proxy affinity. |
| Browser through an unsupported Negotiate proxy returns `400` after `401` | Suspect connection-affinity failure; repeat directly against Kestrel and inspect backend/HTTP logs. |
| `502` or proxy `ECONNREFUSED` | Backend is not listening at the configured loopback target. |
| `/health/live` succeeds but APIs fail | Connectivity alone is not authentication validation. |

## Automated regression strategy

`npm run test:ui` runs `tests/dev-auth-proxy.test.mjs` with **real HTTP traffic**
between the Vite middleware server and a loopback mock backend. It checks
challenge/denial, identity injection, browser-header spoof resistance, health
header removal, explicit opt-in, invalid identities and rejection of remote
targets. This catches frontend proxy regressions in ordinary PR CI before
developers encounter them.

The existing Windows installer CI validates production packaging, not a domain-
joined real-browser Negotiate handshake. A separate native Windows IWA
browser/intranet validation gate is still required for release and customer
field deployments, and must be recorded as passed or explicitly not run.

This development facility is independent of the Perforce pending changelist
and should not be copied into the P4 workspace as a private custom proxy.
