# Native Windows browser diagnostic (#92)

## Working checkpoint — after PR #127

PR #127 merged into develop as `d23ef0d445cdd7264ac59ee884a0547dd806afb4`
after all seven jobs passed head `1a13a4d` in run `39004825569`. All seven
post-merge jobs also passed in run `39005201448`.

The manual workflow `.github/workflows/windows-browser-diagnostics.yml` builds
the SPA and native server, provisions a disposable PostgreSQL database and native
Windows user/group, and invokes `tools/ci/windows-browser/Invoke-WindowsFixture.ps1`.
It has not yet established Windows browser coverage. No authentication
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

## Manual workflow and evidence limits

Run **Native Windows browser diagnostics** from Actions on the diagnostic branch
once GitHub has registered this workflow on the default branch. Alternatively, explicitly add the `run-windows-browser-diagnostics` PR label
on a same-repository PR to request the first diagnostic before default-branch
registration. Remove and re-add the label to request another run after changes;
pushes do not silently rerun it. The workflow handles only manual dispatch or
this explicit label event; regular CI does not execute the native fixture.
Fork PRs cannot opt in. The connected GitHub tools currently do not expose
workflow dispatch, so the label path allows the diagnostic to be requested
without promoting untested workflow code to the default branch. No native run
is claimed from portable parsing, a green regular CI run, or creating this
workflow.

The fixture requires a disposable GitHub-hosted Windows 2025 runner. It uses the
runner's PostgreSQL 17 installation, creates a uniquely named database, starts
checksum-pinned NATS, and serves the built application directly from a known
Kestrel process in Negotiate mode. CI-only diagnostic logging is opt-in and
refuses non-CI environments or another authentication mode.

The generated Windows password exists only in memory and a credential object
used to create the fresh native process; it is never passed in arguments,
environment variables or files. The child verifies its expected native user SID
and enabled local group SID before any authentication probe. These checks prove
the local token fixture, not WSA group-grant authorization or AD membership.

`fixture.json` records native baseline results, policy outcomes, opaque server
correlation/connection observations, cleanup status and a distinct domain
NOT EXECUTED result. The standalone browser reports retain their explicit
caller-unverified topology/connection labels; use the fixture report for the
combined evidence. A successful native workflow requires both policies and all
scenarios, a 401 Negotiate challenge, an authenticated 200, a matching final
server observation for every reported browser fetch, and successful cleanup.
A failed default policy remains a failed result even when an explicit allowlist
works. Inspect per-call correlation IDs alongside server observations to compare
challenge progression and connection reuse; the presence of observations alone
does not diagnose the cause.

After every run the workflow also publishes the sanitized outcome as GitHub
annotations and a job summary (`Write-DiagnosticSummary.ps1`): fixture failure
stage and type, native-session result, per-scenario status and HTTP status counts.
It reads only the three sanitized reports, reduces every value to a safe token,
and never fails the job. The failing stage is therefore readable from the run page
or the check-runs API without downloading the artifact.

Only three named JSON reports are uploaded, for seven days. Raw process logs,
profiles, usernames, SID values, claims, headers, tokens, bodies, screenshots,
HAR and database contents are not uploaded. Process trees, the database, local
account/group, profile and temporary files are removed in `finally`. Unexpected
failures record a fixed failure stage without exporting exception messages.

Before selecting an authentication fix or adding required PR triggers, execute
this workflow and inspect actual challenge/connection progression. Group-grant
mapping, multiple memberships, revocation followed by a fresh logon, and the
separate domain AD/SPN/Kerberos matrix remain follow-up coverage. Local VM success
cannot close the domain gate.

## Primary references

- [Edge AuthServerAllowlist policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/AuthServerAllowlist)
- [GitHub Windows 2025 runner inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)
- [Use WebDriver with Microsoft Edge](https://learn.microsoft.com/en-us/microsoft-edge/webdriver-chromium/)
