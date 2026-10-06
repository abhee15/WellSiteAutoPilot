# ADR-001: Keep V1 Deployable Service Count Small

## Status

Accepted.

## Decision

V1 uses four WellSite AutoPilot application processes:

1. WellSite AutoPilot Server
2. Integration Gateway
3. .NET Worker Host
4. Python Worker Host

PostgreSQL and NATS/JetStream are infrastructure dependencies. Provider Simulator is development/test tooling.

Authentication/authorization, scheduling, audit, readiness, asset management, and module catalog are logical modules within the Server rather than independent microservices.

## Rationale

The deployment is CygNet-adjacent and on-premises. Additional independently deployed services add installation, certificate, networking, monitoring, versioning, and fault-handling complexity without a corresponding V1 benefit.

Boundaries remain explicit in code so selected modules can be extracted later if scale or product requirements justify it.
