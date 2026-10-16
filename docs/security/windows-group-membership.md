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
