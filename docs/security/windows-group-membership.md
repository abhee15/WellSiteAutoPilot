# Windows group membership and paired grants

## Delivery boundary

The access completion scope includes Windows/AD users, membership in multiple
AD security groups, direct and group role assignments, custom roles and their
combined effective access (#76 and #77). Direct-only authorization does not
complete that scope.

This slice provides trusted token-group resolution and pure/SQL grant
composition. It does **not** register a new live authorization path. Group
assignment administration, runtime policy/capability integration, direct
recovery-administrator protection and removal of legacy authority still need
end-to-end implementation. Existing callers without explicit membership proof
continue to ignore group assignments.

## Trusted source

`WindowsTokenGroupResolver` accepts only the primary authenticated native
`WindowsIdentity` under the configured Negotiate provider. The native user SID
must match the externally resolved SID identity before membership can be bound
to the persisted local user ID. Generic claims, `GroupSid` claims, secondary
identities, test headers and client-supplied group names are not membership
proof. A non-Windows or unsupported identity returns no group proof; it does
not fabricate an empty administrative membership.

Enumerate token groups and retain only SIDs for which that token's
`WindowsPrincipal.IsInRole(SecurityIdentifier)` is true. Token group collections
can include deny-only/disabled groups, so copying the whole collection would
be unsafe. Group names are display metadata; canonical SID values match the
persisted group assignments.

No LDAP dependency, credentials, directory synchronization, cross-request
membership cache or user impersonation is introduced. Optional directory
lookup/browsing is a separate adapter capability, not an Internet dependency.

## Scope and lifetime

`VerifiedWindowsGroupMembership` is an immutable, bounded snapshot associated
with one local user. Pure and SQL evaluators require its matching user and an
explicit evaluation time. Future-dated or older-than-one-minute observations
cannot contribute group grants; direct grants remain independently effective.
A revoked grant/role or inactive product user/Asset is checked in SQL on each
query execution. Both resource-scoped and deployment-wide permissions keep
subject, role, permission and scope within the same grant.

The one-minute bound limits reuse of **our proof object**. It does not make
AD membership changes appear in an existing Windows access token. There is no
live directory query here. Group removal, nested-group changes and account
revocation reach the application when Windows authentication provides a renewed
token. Test and document actual reauthentication/token behavior on the target
Windows/AD deployment under #92 before claiming a directory revocation latency.
Do not report token observation time as directory verification time.

## Verification

- Pure tests cover two groups with disjoint roles/Assets, mixed direct and
  group grants, provenance, malformed/expired/wrong-user proof, immutable
  membership snapshots and inactive-user denial.
- PostgreSQL tests compose the same group proof across Assets, all disclosed
  configuration revisions/input sources, V1/V2 execution scopes and target
  Recommendations. They verify SQL pagination and group/direct scope isolation,
  missing/expired proof, changed membership, revoked assignments and global
  permission scope compatibility.
- Windows CI inspects an actual local process token and compares enabled group
  membership; Linux tests prove generic/header claims cannot substitute for it.
  A local Windows process-token test is not a browser Negotiate handshake or
  evidence of domain/nested-group/reauthentication behavior.

## Assignment administration

Administration → Scoped grants supports both individual users and Windows
security groups. Select Windows group, enter the group's SID obtained from
Windows/AD, then load its assignments. The role/permission preview, explicit
Asset scope, final review, durable readback and revision-fenced retirement
are shared with direct assignments. Built-in and custom roles use the same
catalog; resource and deployment permissions remain paired with each grant's
scope. Group names are not identifiers. SID syntax validation does not prove
that a group exists in the directory, or that the current user belongs to it.
Directory browsing is not required for this MVP slice.

Group endpoints are `/security/windows-groups/{groupSid}/grants` (GET/POST)
and `/{grantId}/deactivate` (PUT), under `/api/v1`. They require the existing
active administrator authority. Both service and transactional repository
validate roles/scopes; writes audit the subject kind, canonical SID, actor,
role/revision and mutation in the same transaction. An active subject/role/
scope assignment is unique. Failed audit writes roll back creation and
retirement. No automatic write retries occur in the UI.

These records remain **proposed access**: neither entering a SID nor assigning
a role changes the administrator's or any member's current legacy authority.
The runtime replacement must add group-aware self-change checks, last-direct-
administrator protection and request-time revocation before activating these
grants. Do not wire group grants into legacy claims independently of those
protections. Multi-group and direct/group evaluation is already tested at the
pure evaluator and SQL resource-query boundary; this slice adds authoring,
not a claim that the complete AD/domain runtime has shipped.

The schema change adds only a filtered uniqueness index for active group
assignments. It contains no backfill or rollout state machine. Unreleased dev
snapshots containing manually inserted duplicate active group assignments
should be reset/reseeded explicitly rather than preserved through a data
repair campaign.
