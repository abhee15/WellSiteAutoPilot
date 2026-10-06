# ADR-005: Non-invasive CygNet and WAMI integration

## Status

Accepted.

## Context

WellSite AutoPilot depends on CygNet for SCADA/process data and governed control operations and on WAMI for engineering calculations. Coupling AutoPilot delivery to product changes inside CygNet or WAMI would increase deployment coordination, version coupling, field-upgrade risk, and validation scope.

## Decision

WellSite AutoPilot shall integrate with CygNet and WAMI through their existing supported interfaces. Normal AutoPilot deployment shall not require AutoPilot-specific software development in either provider product.

The responsibility boundary is:

```text
WellSite AutoPilot Core
        |
Integration Gateway
   |             |
CygNet Adapter   WAMI Adapter
   |             |
existing         existing
supported        supported
interfaces       service contract
```

AutoPilot-specific adaptation belongs to WellSite AutoPilot.

For CygNet, the adapter owns translation between AutoPilot semantic contracts and supported CygNet APIs/SDK capabilities, including provider-version compatibility.

For WAMI, the adapter consumes the supported WAMI service contract. WAMI remains responsible for its own server/worker routing and COM-dependent calculation workers. AutoPilot does not route directly to WAMI workers.

## Configuration is allowed

This decision does not prohibit deployment or integration configuration. AutoPilot may require endpoint configuration, Windows/service identities, provider permissions, certificates or transport-security configuration, CygNet point/command mappings, WAMI connection configuration, compatibility validation, and provider-side enablement already supported by the provider product.

These are configuration activities, not provider product development.

## Missing capabilities

If AutoPilot requires a capability that is not exposed through an existing supported CygNet or WAMI interface, that condition must be represented as an explicit integration gap.

The AutoPilot architecture must not silently assume that CygNet or WAMI will be modified to close the gap. Any exception to this ADR requires a new architecture decision and explicit product-owner agreement.

## Consequences

- The WellSite AutoPilot core remains provider-neutral.
- Provider-specific versioning remains isolated to adapters and compatibility validators.
- AutoPilot can be upgraded independently where supported provider contracts remain compatible.
- Simulators model provider-observable behavior rather than reimplementing provider internals.
- Provider product roadmaps are not implicit dependencies of normal AutoPilot feature development.
