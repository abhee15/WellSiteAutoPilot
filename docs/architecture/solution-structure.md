# Solution Structure and Dependency Rules

## Planned repository layout

```text
src/
  server/
    WellSiteAutoPilot.Server
    WellSiteAutoPilot.Application
    WellSiteAutoPilot.Domain
    WellSiteAutoPilot.Infrastructure

  integration/
    WellSiteAutoPilot.IntegrationGateway
    WellSiteAutoPilot.Integration.Contracts
    WellSiteAutoPilot.Integration.CygNet
    WellSiteAutoPilot.Integration.Wami

  workers/
    WellSiteAutoPilot.Worker.DotNet
    python/

  sdk/
    dotnet/
    python/

  simulators/
    WellSiteAutoPilot.ProviderSimulator

  web/

tests/
  Unit/
  Integration/
  Architecture/
  EndToEnd/
  Simulator/

installer/
  nsis/
  bootstrapper/

docs/
  requirements/
  architecture/
  adr/
```

## Dependency direction

```text
Domain
  <- Application
      <- Infrastructure
          <- Server host

Integration.Contracts
  <- CygNet adapter
  <- WAMI adapter
  <- Simulator implementations

ACL SDK
  <- Logic Modules
```

### Forbidden dependencies

- Domain must not reference EF Core, NATS, SignalR, HTTP, CygNet, WCF/CoreWCF, or provider libraries.
- Logic Modules must not reference CygNet/WAMI implementation libraries.
- Server business modules must not reference the concrete CygNet implementation assembly.
- Workers must not access WellSite AutoPilot business tables directly.
- Browser code must not hand-code transport DTOs that duplicate generated API contracts.
- WAMI/CygNet credentials must not enter Logic Module execution context.

## Server internal modules

The Server remains one deployable process while keeping boundaries for:
- Assets / Asset Types / Asset Groups
- Logic Catalog / Logic Configuration
- Scheduling / Execution
- Recommendations / Control
- Readiness / Policy
- Authorization
- Licensing
- Activity / Audit
- System Health

Extraction into separate network services is a future option, not a V1 requirement.

## Provider contracts

Prefer narrow capability contracts rather than one giant provider interface:
- current data
- historical data
- alarms
- asset discovery
- calculations
- commands
- verification
- provider health

WAMI is an Engineering Calculation Provider. CygNet provides SCADA/data/control capabilities.

## Persistence ownership

V1 begins with one WellSite AutoPilot DbContext and one PostgreSQL database. EF mappings are organized by owning module. Cross-module table access should not become the normal integration mechanism.

Configuration and completed operational records reference explicit immutable revision/version identifiers.
