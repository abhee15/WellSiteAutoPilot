# Native Windows browser diagnostic (#92)

## Working checkpoint — after PR #127

PR #127 merged into develop as `d23ef0d445cdd7264ac59ee884a0547dd806afb4`
after all seven jobs passed head `1a13a4d` in run `39004825569`.

`tools/ci/windows-browser/Invoke-BrowserProbe.ps1` is a browser probe under
construction. It has not established Windows browser coverage. No authentication
architecture change or request-serialization workaround is selected.

The probe uses the installed Microsoft Edge WebDriver and its real browser,
under the invoking Windows process identity. It supplies no browser HTTP
credentials, synthetic identity headers, saved authentication state or forms.
The authenticated browser identity must match the native Windows process user.
A final 401, generic browser credentials or a rendered login prompt is not proof
of Windows Integrated Authentication.

Each scenario starts a new browser profile. Equivalent origin policy is tested
in separate invocations so a successful warm session cannot mask a cold failure.

| Scenario | Start | Required evidence |
| --- | --- | --- |
| Identity navigation | Fresh browser directly opens `/api/v1/security/me` | Native Windows identity matches the returned identity |
| Sequential | Anonymous health page, then one API fetch at a time | Identity match and final statuses for all configured routes |
| Cold concurrent | Anonymous health page, then parallel identity and resource calls | Per-call final status and opaque correlation ID |
| Warm concurrent | Successful native identity navigation, then parallel calls | Compare additional connections with the cold case |
| SPA | Fresh browser loads the Kestrel-hosted built application | Authorized navigation loads, identity matches, resource calls finish |

The caller must use a disposable bootstrap administrator for the full resource
matrix. A 403 from an authenticated user without grants is an authorization
result, not evidence that the browser handshake failed. Separate denied-access
and group-mapping cases belong to the following coverage expansion.

## Local probe use

Run the existing native baseline first, under the same Windows logon:

```powershell
./tools/ci/verify-windows-negotiate.ps1 -BaseUrl http://localhost:5080
./tools/ci/windows-browser/Invoke-BrowserProbe.ps1 -BaseUrl http://localhost:5080 -Policy Default -OutputPath ./diagnostics/default.json
./tools/ci/windows-browser/Invoke-BrowserProbe.ps1 -BaseUrl http://localhost:5080 -Policy OriginAllowlist -OutputPath ./diagnostics/allowlisted.json
```

Record default-policy failures; do not silently replace them with the allowlist
result. Edge normally detects intranet servers when AuthServerAllowlist is not
configured, and policy changes require browser restart. The explicit probe uses
an origin-specific browser launch argument; its observed behavior must be
reported rather than assuming that it overrides managed policy.

The report deliberately marks topology as caller-unverified and connection
evidence as missing. Browser scenario success alone does not complete #92.
Domain coverage remains NOT EXECUTED in the local fixture.

## Remaining before a runnable diagnostic PR

- Add a manual-only Windows workflow with a disposable database, pinned NATS,
  built SPA served directly by Kestrel in Negotiate mode, and disposable local
  Windows user/group provisioning. Never use TestHeader for any auth baseline.
- Launch the baseline and browser probe under that fresh Windows logon; verify
  its native user SID and enabled group SID before browser startup. Keep the
  generated password only in memory and use a Windows credential object for
  process creation, never command-line or environment credentials.
- Integrate the prepared CI-only server observations (`Diagnostics:WindowsAuthentication`)
  with the runner. They contain endpoint name, protocol, status, elapsed time and
  correlation/connection IDs. Portable checks assert that raw header/token values,
  identity claims, paths and query strings do not enter the observation DTO.
- Merge only sanitized reports with Windows/browser versions, actual local
  outcomes and a separate domain NOT EXECUTED result. Do not upload raw process
  logs or database contents. Clean up processes, profiles and local accounts.
- Execute the workflow and inspect real challenge/connection progression before
  selecting any authentication fix or adding required PR triggers.

## Primary references

- [Edge AuthServerAllowlist policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/AuthServerAllowlist)
- [GitHub Windows 2025 runner inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)
- [Use WebDriver with Microsoft Edge](https://learn.microsoft.com/en-us/microsoft-edge/webdriver-chromium/)
