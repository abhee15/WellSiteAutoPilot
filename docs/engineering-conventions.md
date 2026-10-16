# Engineering conventions

This document records the initial, deliberately small set of conventions for WellSite AutoPilot. Prefer repository-native .NET and frontend tooling; do not introduce a custom framework to enforce style.

## MVP development policy (2026-10-16)

The product is in phase-one development; no release branch has been cut.
Prefer the smallest complete implementation that satisfies the agreed design.

- Development databases are disposable. When an intentional schema change
  requires a reset, document the reset and update the development seeds and
  installer/migrator path together. Do not build data-preservation, backfill,
  or reversible migration workflows for unreleased development snapshots.
  Never reset a database automatically or assume an arbitrary database is
  disposable; resets are explicit development operations.
- Remove obsolete code, tables, endpoints and UI when their replacement is
  complete. Avoid parallel legacy/new runtime implementations and speculative
  feature flags, rollout state machines or compatibility adapters.
- Keep authentication, paired permission/Asset authorization, last-admin
  protection, atomic writes, durable messaging and useful audits. MVP scope
  reduces rollout machinery; security and data-integrity invariants remain.
- Clean up code touched by a feature when it reduces duplication or makes a
  boundary easier to understand. Defer repository-wide formatting, generic
  frameworks and file splitting based only on line counts. Extract cohesive
  API registrations or page components when the current change needs them.
- Before cutting a release branch, establish the supported schema/API/message
  baseline and customer upgrade, compatibility and recovery requirements.
  After that baseline, preserve supported customer data and contracts.

### Current cleanup priorities

1. Complete scoped authorization under #76, then remove legacy role/scope
   authority and migration-only administration screens/endpoints together.
   Until that replacement is validated, legacy remains the current authority;
   do not remove only half of an enforcement path.
2. Reuse the existing paired-grant SQL predicate and pure evaluator. Collection
   filtering must precede pagination for Assets, Configured Logic, Executions
   and Recommendations; grant revocation and mutations need one consistent
   database concurrency boundary.
3. Split `Server/Program.cs` route registration and `src/web/src/App.tsx`
   composition along actual feature boundaries as those areas change. Their
   size warrants attention, not an unrelated rewrite ahead of enforcement.

## Backend (.NET 10)

- Follow the root `.editorconfig`. Use `dotnet format WellSiteAutoPilot.slnx --verify-no-changes --no-restore` to inspect formatting drift; apply `dotnet format WellSiteAutoPilot.slnx` to fix it locally. Enable a blocking CI check only after the existing solution has been normalized, to avoid unrelated mass changes.
- Use asynchronous APIs for I/O (database, network, messaging, filesystem); name them with the `Async` suffix. Keep pure mapping, validation, and calculations synchronous.
- Accept and propagate `CancellationToken` for cancellable I/O and background processing. Avoid blocking on tasks with `.Result` or `.Wait()`.
- Prefer dependency injection, structured logging with stable event identifiers, and domain-specific error codes over ad hoc service locators and unstructured exception strings.
- Keep authentication and authorization enforcement server-side; UI permissions are hints, never security boundaries. Control actions require explicit policy validation and auditable identity.
- Add tests for concurrent requests, duplicate delivery, cancellation, and failure recovery where behavior crosses persistence or messaging boundaries.

## Frontend (React / TypeScript)

- Use ESLint, TypeScript checks, and Prettier when the actual frontend workspace is established. Keep lint and format checks separate from application builds.
- Prefer small, accessible, reusable components. Extract thin grid/chart wrappers when concrete screens share behavior; do not create a generic UI framework in advance.
- Make UI strings localization-ready; render times and units using explicit locale/timezone context.
- Preserve responsive layouts and keyboard-accessible controls for industrial panels and desktop use.

## Rollout

1. Establish common whitespace/style conventions and document the expectations.
2. Identify the actual frontend workspace and add tool configurations alongside its package manifest and lockfile.
3. Baseline existing formatting and lint findings; then turn on CI verification in a focused follow-up change.
4. Tighten analyzer severity gradually; avoid introducing widespread unrelated code churn into feature PRs.

## CI branches and pull requests

- Open a draft PR targeting `develop` when a feature branch is ready for CI. Draft PRs receive the full validation suite after each push, including PostgreSQL, JetStream, gateway, API/client, and installer checks.
- The CI workflow validates `main` and `develop` on push for post-merge confidence. Feature, fix, chore, and validation branches do not separately run the duplicate push suite; the PR event is the single source of pre-merge verification.
- Keep a PR draft while implementing; mark ready and merge only after its current head commit passes all required jobs and the feature's relevant regression tests.
- The workflow's per-PR concurrency cancels outdated runs when additional commits arrive, while preserving the latest verification.

## CI enforcement: guard definite violations, review semantic duplication

The lightweight **Engineering conventions** CI job runs for every pull request and for
pushes to `develop`/`main`. It has no third-party dependencies and:

- Checks production `.csproj` dependency boundaries using XML parsing. Domain, contracts
  and application layers must not reference persistence, server, gateway, worker, or
  simulator implementations; the .NET worker must not reference persistence or the
  integration gateway implementation. Missing and out-of-repository project references
  also fail.
- Rejects direct changes to generated TypeScript API clients in PR diffs. The generator
  must remain the source of truth; `src/web/src/api/generated/README.md` is exempt.
- Executes self-tests for the guard, including negative regression cases.

**Not blocking:** duplicate-line percentages, speculative generic abstractions,
or broad pattern matching over source files. These create false positives and cannot
distinguish an intentionally explicit authorization method from drift. Code reviewers
must use `.github/PULL_REQUEST_TEMPLATE.md` to check reuse of repository queries,
validation, saved-view storage, and message lifecycle helpers. When modifying a scoped
query, assert SQL scope filtering precedes `Take` and cover scoped/unscoped parity in
tests. Never replace an explicit security boundary with a nullable authorization sentinel.

Continue using the existing blocking TypeScript strict checks, ESLint, frontend tests,
warnings-as-errors .NET build, PostgreSQL/JetStream tests, and installer validation.
Only turn on blocking `dotnet format` or `npm run format:check` after existing
formatting drift has been baselined in a separate, focused change. Add positive and
negative tests for every new convention rule; exceptions must be narrowly documented.
