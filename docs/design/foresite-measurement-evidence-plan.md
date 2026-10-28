# ForeSite measurement evidence: probe first, parser changes second

Historical plan for #300. The owner's subsequent CygNet datagroup clarification
establishes the two supported controller fields' acquisition contract; see
[the current implementation](../implementation/foresite-controller-acquisition.md).
The probe remains a diagnostic and always labels its reports Unverified. The
original evidence gates below describe the pre-clarification plan and remain
useful for additional, unreviewed source categories.

Baseline: develop `2d0855f` (#289–#299). The approved package, scoped read plans,
Gateway sessions, failure persistence and UI already exist. The remaining live
blocker is field semantics, not a replacement execution or connection system.

## Decision and implementation order

1. Add a bounded operator probe to the existing CLI, reusing `ForeSiteSession`
   and `ForeSiteAdapter.BaseAddress`. Prove it against deterministic HTTP fixtures.
2. Owner runs the probe on-premises with the Gateway's actual Windows account
   and the address/numeric well ID from an existing saved connection/binding.
   This is a direct operator diagnostic, not an application-authorized execution;
   it cannot establish RBAC, saved-connection revision or package readiness.
3. Review the sanitized observations together with independently known device
   update times, timezone, engineering unit and ForeSite quality documentation.
4. Only after review, add a narrowly defined provider mapping with recorded
   fixtures and explicit version/field semantics. Reuse the current parser,
   validation, diagnostics and Shadow E2E. Do not promote timestamp equality,
   retrieval time or a larger freshness limit as measurement evidence.
5. Owner verifies that mapping through the existing saved-connection Shadow
   checklist. Keep provider writes and autonomous control disabled.

## Probe contract

- Command: `foresite probe <well-id> <new-report.json> [samples] [interval-seconds]`.
  Address comes from the existing opt-in `FORESITE_BASE_URL` environment variable;
  no new persistent settings, credential prompt, connection registry or endpoint.
- Use current Windows credentials, existing token/cookie/one-401 retry code,
  GET `TokenService/CreateToken` and GET `Surveillance/well/<positive numeric id>`.
  Refuse redirects; retain normal TLS verification. Never call discovery, write,
  control, scan-trigger or configuration APIs. Do not stop devices for a probe.
- Default 3 observations, 5 seconds apart. Allow 2–12 samples, 1–30 seconds apart;
  10-second budget per observation and 180-second total budget. Cancellation stops
  acquisition. Never retry non-401 failures or change a requested well silently.
- Bound buffered provider data at 1 MiB and JSON depth at 16. Do not persist raw
  responses, headers, cookies, URL, well ID, usernames, credentials, error text,
  arbitrary field names/values or customer identifiers. No raw-output switch.
- Report schema v1 contains local capture times, safe outcome/HTTP status and a
  fixed field allowlist. Numeric PumpFillage values preserve missing/null/type
  and out-of-range facts. Scan/measurement timestamp candidates preserve explicit
  offset and UTC instant; offset-free times remain explicitly unverified. Unknown
  strings are classified without exporting their text. Unit/quality candidates
  retain only recognized labels or bounded numeric status codes, never free text.
- Include fixed candidate fields whether present or absent; unknown fields remain
  excluded. Candidates are hypotheses, not claims those fields exist or have the
  required semantics. A repeated response does not prove device freshness.
- Report always says measurement semantics are unverified. Exit 0 means evidence
  captured, not live-read acceptance. Partial failures remain reportable with safe
  codes; cancellation/invalid arguments/output collision fail without raw errors.
  Do not overwrite an existing report. Share only the sanitized report after local
  review, with scenario notes kept free of customer-identifying details.

## Owner scenario matrix

| Scenario | Read-only observation | Independent evidence needed | Acceptance implication |
| --- | --- | --- | --- |
| Stable known well | Repeated values and scan/time candidates | Device value did not update during capture | Advancing HTTP/scan time alone cannot be accepted as value time |
| Normal device update | Capture spanning an already scheduled update | Actual update instant and source timezone | Identify which field tracks the value and survives UTC conversion |
| Already stale well | Read a well already known to have stopped updating | Last actual update instant; do not alter device operation | Fresh retrieval must not hide an old value |
| Already bad/uncertain well | Read existing bad/uncertain status | ForeSite status definition/documented enum mapping | Unknown or undocumented quality fails closed |
| Missing value | Existing unavailable/null reading or deterministic fixture | Whether no value exists versus transport failure | Missing stays missing; never zero |
| Units and range | Current and average fillage plus explicit unit candidates | UI/API documentation of percent/fraction and valid range | Define conversion/range per quantity; no guessed scaling |
| Timezone/offset | Compare returned offsets with source configuration | UTC/local/DST policy; naive time meaning if applicable | No host-timezone inference; ambiguous time rejected |
| Session expiry | Naturally expired session and another capture | Gateway account/session behavior | Existing one-401 retry works; no cookie/error-text leakage |

The first probe supports a fixed set of candidate fields. If the actual value
metadata uses another field, review its name/type locally, then explicitly extend
the allowlist and tests. Do not solve this by exporting the whole Value object.

## CI and change gates

- Fixtures: stable value/moving scan, explicit offset, naive/invalid timestamps,
  quality/unit candidates, missing/null/wrong-type/out-of-range fields, extra
  secrets/customer fields, malformed/deep/oversized responses, error HTML,
  token refresh and refresh failure, virtual directory and caller cancellation.
- Assert GET-only routing, same session cookie, bounded sample policy, safe
  serialized report and existing report preservation. Existing ForeSite tests,
  CLI module commands and architecture checks must remain green.
- Live field mapping stays blocked until the scenario matrix has evidence. Record
  which cases remain unobserved; deterministic mocks prove behavior, not ForeSite
  compatibility. No production parser or authorization change in the probe PR.

## Evidence review record (complete after local capture)

Record build SHA, anonymous probe ID, scenario, independently observed update
interval, candidate value/time/quality/unit fields, authoritative field/enum
reference, timezone policy, and conclusion (`confirmed`, `inconclusive`, `rejected`).
Keep the connection ID/revision, binding ID, account identity and source address
in the owner's private local notes; report them only through existing authorized
application diagnostics. A successful parser change requires both a confirmed
mapping and regression fixtures; probes alone never clear the live gate.
