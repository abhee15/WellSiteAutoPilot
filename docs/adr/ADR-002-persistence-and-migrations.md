# ADR-002: EF Core Code First with Controlled PostgreSQL Migrations

## Status

Accepted.

## Decision

- Use EF Core Code First.
- PostgreSQL is the only supported V1 database provider.
- Use a shared/provider-neutral persistence model where practical.
- Do not create fake or empty SQL Server migration chains.
- Keep provider-specific behavior isolated so a real SQL Server provider can be introduced later if required.
- Run production migrations through a dedicated Database Migrator invoked by the installer/bootstrapper.
- Do not call `Database.Migrate()` during normal production Server startup.

## Upgrade behavior

Schema-changing upgrades require Maintenance/Control Inhibit, preflight validation, backup protection, migration, compatibility checks, and health/readiness validation before control can resume.

Runtime database credentials should be separable from higher-privilege migration credentials.
