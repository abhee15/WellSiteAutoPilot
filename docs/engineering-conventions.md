# Engineering conventions

This document records the initial, deliberately small set of conventions for WellSite AutoPilot. Prefer repository-native .NET and frontend tooling; do not introduce a custom framework to enforce style.

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
