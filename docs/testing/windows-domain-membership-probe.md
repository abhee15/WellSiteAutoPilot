# Windows AD selected-user group membership — opt-in diagnostic

**Scope:** Slice 3 feasibility research. This diagnostic is **not an application endpoint**, an AD sync job, a new identity provider, or an authorization engine. It never writes to AD, the WellSite AutoPilot database, roles or grants. It does not impersonate users or accept supplied LDAP servers, credentials, domain names or arbitrary LDAP queries.

See [effective-access inspection feasibility](../security/effective-access-inspection-feasibility.md) and [Windows membership runtime](../security/windows-group-membership.md).

## What the probe tests

`tools/ci/windows-domain/Invoke-SelectedUserMembershipProbe.ps1` uses the **Windows process's integrated identity** on a domain-joined machine. Given **one canonical target user SID** and **one canonical expected group SID**:

1. Verify Windows, explicit opt-in, the local joined domain and canonical SID values.
2. Translate the binary user SID into an **escaped LDAP objectSid assertion**; query the joined domain for **at most two matches** and require exactly one user.
3. Perform **one BASE-scope read** of that user's AD computed `tokenGroups` attribute, with bounded directory search timeouts (default 5 seconds, maximum 15), and reject empty, missing, malformed or larger-than-2048 SID results. Return only SID count and whether the **one expected SID** appeared in the AD snapshot.
4. Only when the process's native Windows SID **equals the target SID**, compare that same expected group to `WindowsPrincipal.IsInRole(SID)` for the **current process token**. Never impersonate or use the operator's token to infer another user's Windows access.
5. Print a **single sanitized JSON record** with fixed state, timing/counts and optional booleans, not group lists, user/group SIDs, DN, usernames, hosts, credentials, token contents or exception messages.

Microsoft states that `tokenGroups` is computed from transitive group memberships, requires a Global Catalog and can be expensive on domain controllers. It is **different** from the actual Windows token's enabled/deny-only SIDs. See the primary references below.

## Safe invocation

Run **manually** in a controlled on-prem Windows/AD test environment with approved **least-privilege directory-read access**, not on a public CI runner or the production service. Do not invoke on every HTTP request or on a list of users. Perform the probe only after the domain owner approves a bounded test, using a **test subject and test security group**.

First run the CI-safe **domain-free** self-test (this is also checked by the Windows Foundation CI job):

```powershell
./tools/ci/windows-domain/Invoke-SelectedUserMembershipProbe.ps1 -SelfTest
```

Expected: `State=SelfTestPassed`, `LiveDirectoryQueried=false`, `CanAuthorize=false`. This only validates the SID-to-LDAP binary assertion and output safeguards; it is **not AD evidence**.

Then, **only** after approval, supply the target's verified canonical SID and one known group SID as local session variables:

```powershell
# Values must come from your approved test fixture, not from this example.
$targetUserSid = '<canonical test user SID>'
$expectedGroupSid = '<canonical test group SID>'

./tools/ci/windows-domain/Invoke-SelectedUserMembershipProbe.ps1 `
  -TargetUserSid $targetUserSid `
  -ExpectedGroupSid $expectedGroupSid `
  -EnableLiveDirectoryRead `
  -TimeoutSeconds 5
```

Avoid entering real identities into shared CI logs, support tickets or recordings. Handle local console transcripts and the resulting membership boolean/count as sensitive, and delete them after review. LDAP *search* timeouts are not a hard deadline for every DNS/bind operation; run under an external process-level timeout and coordinate with the AD administrator.

## Interpret the result precisely

| JSON field | Meaning |
| --- | --- |
| `State=DirectorySnapshotOnly` | One target was found, a nonempty bounded AD group result was decoded. **Not a Windows authorization result.** |
| `State=Unavailable` or `InvalidInput` | Unsupported/unavailable query or invalid parameters. No assertion about group membership or absence. Exit code is 2. `FailureStage` names only a fixed, sanitized stage. |
| `ExpectedGroupInAdSnapshot` | Whether the expected SID appeared in that single computed AD snapshot. Even `false` is **not** proof of no effective access. |
| `AdGroupSidCount` / `DirectoryReadMilliseconds` | Bounded local observations useful for checking feasibility and cost. They do not establish directory freshness or a latency SLA. |
| `ProcessTokenComparison=SameWindowsProcessOnly` | Target SID matched the invoking Windows process; `ExpectedGroupEnabledInProcessToken` then reports enabled membership for **that process token only**. It does not represent a browser/Negotiate session. |
| `ProcessTokenComparison=NotComparable` | Selected user is someone else. No token inference is possible. |
| `DirectoryCompleteness=Unproven` | Deliberately constant: local domain/forest, trust, domain-local scope, Global Catalog, replication and other provider semantics are not proven complete. |
| `ProvesEffectiveAccess=false`, `ProvesGroupAbsence=false` | Deliberately constant. Neither positive AD SID discovery nor negative discovery authorizes or denies access to WellSite AutoPilot. |

A missing or empty `tokenGroups` result is **unavailable**, not a verified empty membership set. More than 2048 returned SIDs is **unavailable**, never silently truncated. A SID outside the local domain's search scope yields no supported cross-forest verdict.

## Owner-run acceptance matrix

| Controlled case | What to record and compare | Release status |
| --- | --- | --- |
| Direct security group | Test target in group, AD snapshot positive; separately validate authenticated Windows token on server | Requires actual domain and browser evidence |
| Nested security group | Confirm whether the joined-domain query captures approved transitive group, including the known scope | Requires domain evidence |
| Domain-local/cross-domain/trusted forest | Examine missing/partial domain coverage, do **not** interpret absence as denial | Requires domain topology and permission review |
| Group removed, existing browser session retained | Compare AD snapshot with old browser token and a **new** logon; report actual renewal/revocation behavior | Requires controlled membership change and session renewal |
| Disabled/deny-only SID | Contrast AD membership with `WindowsPrincipal.IsInRole` for an actual test token | Requires native Windows test evidence |
| GC outage, missing directory rights, absent target, huge group set | Only `Unavailable`; no stale grants or false no-access verdict | Requires negative-path evidence |
| Large enterprise directory | Run a single bounded test and record duration/DC impact with domain administrators; do not load test production | Requires AD capacity evaluation |

The Windows CI self-test does **not** execute this matrix. The existing native Windows/browser diagnostics similarly do not prove a real joined-domain Kerberos/NTLM session.

## Decision gate for the eventual API

A directory SID snapshot is potentially useful as a **read-only informational explanation** (e.g. “Directory snapshot contains group SID X”), but it is **insufficient** to assert that another user currently has a Windows-enabled group in a live authenticated token.

Do **not** promote the returned SIDs to `VerifiedGroupMembership` or feed them into the runtime grant evaluator as if they were an authenticated user's enabled token. Do not add a `VerifiedNoAccess` outcome to the Users page from this diagnostic. Keep **Group-derived access: Not evaluated** until provider-specific evidence completeness, freshness, domain scope, and authorized inspection have been proved separately.

## Sources

- [Microsoft: computed tokenGroups, GC requirement and performance](https://learn.microsoft.com/en-us/windows/win32/adschema/a-tokengroups)
- [Microsoft: tokenGroups includes direct, indirect and primary group SIDs](https://learn.microsoft.com/en-us/windows/win32/ad/security-properties)
- [Microsoft: TOKEN_GROUPS SID attributes, enabled and deny-only flags](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_groups)
- [Microsoft: CheckTokenMembership verifies an enabled SID in a token](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-checktokenmembership)
- [Microsoft: tokenGroupsGlobalAndUniversal limits](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/a366b6b6-f36f-46e8-ba2d-af79bf8966bf)
