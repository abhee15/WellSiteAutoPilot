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

## Pre-release development exception (2026-10-16)

Before the first release branch, development schemas and seed data may be
replaced rather than upgraded in place. Generate the EF model/migrations with
the repository tooling, update seeds and tests together, and verify a clean
PostgreSQL install through the migrator. Document any required explicit reset
of a disposable development database. Do not add backup/conversion/rollback
machinery solely to preserve unreleased development snapshots, and do not
reset or migrate databases automatically during normal Server startup.

At release, establish the schema baseline and apply the customer upgrade
requirements below.

## Upgrade behavior

Schema-changing upgrades require Maintenance/Control Inhibit, preflight validation, backup protection, migration, compatibility checks, and health/readiness validation before control can resume.

Runtime database credentials should be separable from higher-privilege migration credentials.
