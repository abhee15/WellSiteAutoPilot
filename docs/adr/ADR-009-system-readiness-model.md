# ADR-009: One system readiness model

## Status

**Proposed.** Needs owner approval before roadmap step 7. It must be settled before the control path
depends on it.

## Context

Maintenance mode, control inhibit, the health page and the Overview all need the same answer to "is the
system ready, and for what". Without one model each would compute its own and they would disagree.

## Decision

1. **Readiness is computed once on the Server** from named checks (database, messaging, each worker
   runtime, each provider, module catalog, licensing, maintenance state) and exposed as one read model.
   Each check reports `Unknown`, `Healthy`, `Degraded` or `Unavailable`, a reason code and when it was last
   observed. Missing or stale evidence is `Unknown`, never `Healthy`.
2. **Readiness is per capability, not one flag.** The model answers separately for: viewing data,
   running Shadow, producing Recommendations and (later) issuing control. Each capability lists the checks
   it requires. Control requires the strictest set and fails closed.
3. **Maintenance mode is an input.** An administrator with a dedicated permission can enter it with a
   reason and optional end time; it is audited. It inhibits new executions and all control, lets running
   work finish or cancel, and upgrades enter it before they stop services.
4. **Control inhibit is derived, not stored separately.** Control is allowed only when the control
   capability is ready and maintenance mode is off. There is no second switch that can drift.
5. **Consumers read the model.** The Overview, health page, maintenance banner and the control path call
   the same read model; none reimplements a check.
6. **The audit and activity read API** (roadmap step 7) is separate from readiness but ships beside it, with
   the same scope-before-pagination rule as other lists.

## Consequences

- The control path has one gate to test and review.
- Adding a provider or worker adds a check and a capability requirement, not new UI logic.
- Health data must carry timestamps so staleness is visible.
