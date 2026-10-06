# WellSite AutoPilot Architecture Baseline

## Architecture style

WellSite AutoPilot V1 uses a small set of deployable processes with strong internal module boundaries. The application server is a modular monolith; provider communication and Logic Module execution are isolated into dedicated processes.

### Production processes

1. **WellSite AutoPilot Server**
   - REST API and OpenAPI
   - SignalR
   - authentication integration
   - authorization and resource scopes
   - assets and asset types
   - module catalog and configured logic
   - scheduling and execution orchestration
   - readiness and policy
   - recommendations and control governance
   - audit, activity, licensing status, and system health

2. **Integration Gateway**
   - provider routing
   - CygNet adapter
   - WAMI adapter
   - secure provider credentials
   - current/history/alarm/calculation operations
   - governed command execution and readback verification
   - provider health, compatibility, rate limiting, and idempotency

3. **.NET Worker Host**
   - isolated execution of C# Logic Modules

4. **Python Worker Host**
   - isolated execution of Python Logic Modules

### Infrastructure

- PostgreSQL is the WellSite AutoPilot application system of record.
- NATS + JetStream provide messaging and durable work delivery.
- CygNet/provider historians remain authoritative for process telemetry/history.
- Redis is not required for V1.

### Development/test process

- Provider Simulator Host
  - CygNet provider-contract simulation
  - WAMI CoreWCF/net.tcp simulation
  - deterministic scenarios, fault injection, and virtual time

## Control boundary

Logic Modules calculate desired behavior but cannot directly execute physical control.

```text
Logic Module
  -> ControlIntent
  -> WellSite AutoPilot Server
  -> policy/readiness/control-ownership validation
  -> durable ControlAction
  -> Integration Gateway
  -> provider adapter
  -> CygNet/device
  -> readback/verification
```

Workers may perform authorized reads/calculations through the Integration Gateway but do not receive direct control-execution capability.

## Communication

- Browser -> Server: HTTPS REST + SignalR.
- Server -> Workers: durable JetStream work messages.
- Workers -> Server: durable execution results/events.
- Worker -> Gateway: bounded synchronous internal calls for reads/calculations.
- Server -> Gateway: durable governed ControlAction path.
- Gateway -> WAMI: CoreWCF over net.tcp.
- Gateway -> CygNet: supported CygNet integration APIs.
- Server -> PostgreSQL: EF Core.
- Server/processes -> NATS: authenticated, subject-scoped connections.

## Provider product boundary

WellSite AutoPilot is a non-invasive consumer of CygNet and WAMI capabilities.

- AutoPilot-specific behavior is implemented in WellSite AutoPilot adapters, orchestration, workers, policy, and UI.
- CygNet and WAMI are not expected to add AutoPilot-specific APIs, services, database objects, worker types, or product code.
- CygNet integration uses existing supported CygNet interfaces and capabilities.
- WAMI integration uses its existing supported service contract; AutoPilot does not participate in or replace WAMI's internal worker routing.
- Deployment may require configuration of endpoints, service identities, permissions, certificates, provider mappings, or compatibility settings.
- If a required capability is absent from an existing supported provider interface, it is recorded as an explicit integration gap and does not silently become an assumption that the provider product will be modified.

This boundary keeps the AutoPilot core provider-neutral and prevents provider release cycles from becoming an implicit AutoPilot feature dependency.

## Reliability

- Durable operations use stable identifiers and idempotent consumers.
- PostgreSQL-to-JetStream publication uses an Outbox pattern.
- Critical consumers use Inbox/idempotency records.
- Failure degrades toward no new control.
- Restart recovery revalidates physical state before durable workflows resume.
- CygNet/controller operation remains independent if WellSite AutoPilot is unavailable.

## Security

- User authentication is initially enterprise Windows/AD.
- Authorization is WellSite AutoPilot RBAC + product-defined permissions + resource scope.
- Backend enforcement is authoritative.
- Service-to-service calls use service identity plus execution grants/capabilities.
- Provider credentials are held by the Integration Gateway, never Logic Modules.
- Browser never talks directly to PostgreSQL, NATS, workers, CygNet, or WAMI.
- PostgreSQL and NATS are local-only by default when deployed on the Runtime Node.
- HTTPS is required for production browser access.

## Persistence

- EF Core Code First.
- PostgreSQL is the only V1 supported provider.
- Persistence design avoids unnecessary PostgreSQL-specific coupling so SQL Server can be added later if required.
- Production migrations run through a dedicated migrator/bootstrapper path, not automatic Server startup.
- Completed execution/control/audit history is preserved and correlated to immutable configuration revisions.

## API contracts

- ASP.NET Core REST APIs generate OpenAPI.
- TypeScript transport DTOs and API clients are generated from OpenAPI.
- Generated code is isolated and never hand-edited.
- Frontend-only view/workflow state may remain handwritten.

## Observability

- OpenTelemetry from the first implementation slice.
- Stable business CorrelationId is separate from technical TraceId.
- W3C trace context propagates through NATS.
- Audit and operational history are authoritative in PostgreSQL.
- Technical logs, metrics, and traces are diagnostic and must not become control dependencies.
- Aspire Dashboard is for development, not a required production component.

## Deployment

- Windows services/processes installed through NSIS + a WellSite AutoPilot-owned .NET bootstrapper.
- Controlled .NET and Python runtimes are preferred for offline deployment.
- Product upgrades enter Maintenance/Control Inhibit before binaries or schema are changed.
- CI should continuously produce installable Windows artifacts once the application skeleton is present.
