# Contributing

## Branch model

- `main` is stable and releasable.
- `develop` is the integration branch.
- Work is performed on `feature/*`, `fix/*`, or `chore/*` branches and merged through pull requests.
- Feature pull requests normally target `develop`.
- Release pull requests target `main`.

## Engineering rules

- Do not bypass backend authorization with UI-only checks.
- Do not allow Logic Modules to call CygNet, WAMI, PostgreSQL, or NATS directly.
- Do not allow Logic Workers to execute physical control commands directly.
- Do not hand-edit generated TypeScript API clients or transport DTOs.
- Do not run production EF migrations automatically during normal application startup.
- Do not commit credentials, certificates, customer data, or production endpoints.
- Keep provider-specific code behind integration contracts.
- Preserve correlation IDs across APIs, messaging, workers, and integrations.

## Pull request expectations

Each PR should include:
- a concise description of the change;
- tests appropriate to the change;
- architecture impact when a boundary or contract changes;
- migration notes when persistence changes;
- installer/deployment notes when runtime topology changes.
