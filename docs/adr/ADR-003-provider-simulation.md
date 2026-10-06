# ADR-003: Provider Simulation Strategy

## Status

Accepted.

## Decision

WellSite AutoPilot will provide deterministic development/test simulation without attempting to reproduce complete external products.

### WAMI

The WAMI simulator should implement the relevant modernized CoreWCF service contract over net.tcp so the WellSite AutoPilot WAMI Adapter exercises the production communication boundary.

The simulator does not initially reproduce WAMI's COM worker pool. WAMI remains responsible for its internal worker allocation and COM execution.

### CygNet

CygNet simulation occurs at the WellSite AutoPilot provider-contract level. The simulator provides the semantic capabilities AutoPilot consumes, including current/history values, quality, alarms, discovery, setpoints, commands, readback, manual intervention, latency, and failures.

Real CygNet integration still requires certification against a non-production CygNet environment.

### Scenario engine

The simulator should support deterministic scenario definitions, fault injection, and accelerated/virtual time so long-running workflows can be tested quickly.
