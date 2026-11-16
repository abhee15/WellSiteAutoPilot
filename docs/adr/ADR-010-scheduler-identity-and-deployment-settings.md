# ADR-010: Scheduler identity and deployment settings

## Status

**Accepted by owner direction (2026-11-16):** choose the best long-term solution, then implement without waiting
for approvals. Implementation lands in slices (see Rollout).

## Context

Scheduled runs have no signed-in person, but live provider reads are authorized against an identity. Today that
identity is the setting `Scheduler:ReadAuthority:ServicePrincipalUserId`, resolved by
`ScheduledReadAuthorityProvider`, which re-checks on every occurrence that the user exists and is active (fail
closed) and audits every refusal and use. Nothing in the installer sets it, so installed Windows deployments
refuse scheduled live reads with `EXECUTION_READ_AUTHORITY_REQUIRED`. The AppHost did not pass it to the Server
either (fixed for local development only in draft PR 592).

This is separate from provider access. The Integration Gateway calls ForeSite and CygNet under its own Windows
account (see `installer/README.md`, "The Integration Gateway's account"). That account is unaffected by this ADR.

Principles that apply: automation uses service identities kept separate from personal accounts; one all-access
service identity violates least privilege; scheduled, nobody-online work is the standard use for a service
identity; using a person's own user id is acceptable only for local testing because it breaks when that account is
disabled.

## Options

1. **One system Scheduler user**, created at install, read grants only. Simplest, works out of the box, broad.
2. **Per-schedule owner identity.** Least privilege and clear audit, but breaks when the owner is removed and is
   risky for Autonomous mode.
3. **Per-schedule or per-group service identity chosen by an administrator.** Most flexible, most to build.

## Decision

1. **Build option 1 for the MVP behind `IScheduledReadAuthorityProvider`**, so options 2 and 3 can be added
   without API or UI changes. The seam is the provider's inputs: it already receives the configured logic id,
   revision id and occurrence time, which is enough to select an identity per schedule later. The caller keeps
   treating the result as `DelegatedReadAuthority`.
2. **The Scheduler user is an ordinary `security.users` row** whose scoped grants bound what schedules may read.
   It has no ambient authority and no interactive sign-in. It receives read grants only; it never receives
   control, approval or `settings.manage` permissions.
3. **The installer creates or accepts the Scheduler user** and remembers the choice across upgrades, the same
   way it remembers the Gateway account. Silent install and bootstrapper options follow the Gateway's pattern.
4. **The setting is deployment-level and admin-editable**, stored in the database as an override of the
   configuration value, changed only through the Configuration page or its API, and audited in the same
   transaction as the change. Configuration file or environment values remain the fallback and the
   development path.
5. **Absence is surfaced, not hidden.** A missing, unset, inactive or deleted Scheduler user shows in the system
   readiness check (ADR-009) and as the "Not running" reason on affected schedules. Runs fail closed with the
   existing refusal codes; nothing falls back to another identity.
6. **Disabling or deleting the user revokes delegation on the next occurrence** (already the provider's
   behavior). No in-flight read continues under a revoked identity beyond its pinned read plan.

## Deployment settings framework (phase 1)

The Scheduler identity is the second deployment-level setting after the CygNet path, which justifies building
the Administration > Configuration page from the earlier settings design:

- Preferences are for people; Configuration is for the deployment.
- One typed registry declares each setting: key, type, validation, default, whether it is editable, and the
  permission that guards it.
- The database stores overrides only; the registry default and configuration sources supply everything else.
- `settings.read` and `settings.manage` permissions, enforced on the backend, never only in the UI.
- Every change is audited in the same transaction, with before and after values (no secrets).
- Difference from the earlier design: the CygNet path stays **read-only**; the Scheduler identity is
  **admin-editable and audited**.

## Consequences

- Installed deployments run scheduled live reads out of the box once the Scheduler user has grants.
- One broad identity is a known trade-off, bounded by read-only grants, per-occurrence re-validation and audit.
  Option 3 is the path to narrower identities.
- A new permission pair and a new typed registry become part of the security surface and need tests for scope
  and audit.
- The installer gains a remembered setting and a migration note for upgrades from builds without it.

## Rollout

1. Verify how an installed build receives the identity today (done: it does not; no installer path sets it).
2. Probe grants the identity needs and behavior when it is disabled or deleted.
3. This ADR and the design seam.
4. Settings framework phase 1: registry, storage, permissions, audit, Configuration page, Scheduler identity as
   first editable setting.
5. Installer slice: create or accept the Scheduler user, remember it across upgrades.
6. Merge PR 592 after confirming the "Not running" reason appears.
7. Then overlap and catch-up policies.

## Open items

- Confirm retention for scheduled run history.
- Confirm Autonomous schedules skip stale runs through a bounded catch-up window.
- Confirm the Gateway account on the owner's machine in the Windows services list.
