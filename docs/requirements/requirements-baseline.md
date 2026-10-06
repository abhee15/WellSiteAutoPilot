# Requirements Baseline

This document is an index for the detailed WellSite AutoPilot requirements. Detailed requirements will be split into focused documents as the baseline is consolidated.

## Baseline sections

1. Product scope and core concepts
2. Assets and asset model
3. Logic Module definition and onboarding
4. Configuring Logic
5. Execution architecture and runtime behavior
6. Security, authentication, and authorization
7. Integrations and provider architecture
8. Engineering data, quantities, UOM, quality, and time
9. UI/UX, interaction model, responsiveness, and localization
10. Deployment, installer, Runtime Node, and upgrades
11. Persistence, audit, observability, and nonfunctional requirements

## Locked cross-cutting decisions

- Product name: WellSite AutoPilot.
- CygNet-adjacent on-premises runtime with no normal runtime Internet dependency.
- Shadow, Recommendation, and Autonomous operating modes.
- Logic Modules are provider-neutral and can be Python or C#.
- Asset types are customer-extensible and not hard-coded petroleum enums.
- Logic Modules emit semantic requirements and ControlIntent.
- The platform owns control governance and physical ControlAction.
- WAMI is an external Engineering Calculation Provider over CoreWCF/net.tcp.
- CygNet remains the authoritative SCADA/process source where applicable.
- CygNet and WAMI require no AutoPilot-specific product development for normal integration; AutoPilot adapts to their existing supported interfaces.
- Integration configuration such as endpoints, service identities, permissions, certificates, mappings, and compatibility settings is allowed and is not considered provider product development.
- PostgreSQL is the V1 application system of record.
- NATS + JetStream provide messaging/durable work.
- New configurations default to Shadow.
- Autonomous control is inhibited when authoritative persistence/messaging/provider readiness is unavailable.
- Manual intervention/control ownership is first-class.
- UI is responsive/adaptive and localization-ready from V1.
- REST contracts generate OpenAPI; frontend transport DTOs and API clients are generated.
- NSIS is the V1 installer shell; deployment logic belongs to the .NET bootstrapper.
