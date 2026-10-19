# Provider Adapter Contract Boundary

WellSite AutoPilot provider adapters implement semantic platform contracts. The core application does not depend on CygNet- or WAMI-specific DTOs, assemblies, route names, worker identities, database structures, or version enums.

The initial provider-neutral capability surface is:

- metadata and compatibility;
- health;
- current data;
- historical data;
- alarms;
- asset discovery;
- engineering calculation;
- governed command execution;
- command/readback verification.

An adapter advertises what it actually supports through `ProviderDescriptor.Capabilities`. A provider is not considered command-capable merely because an Asset exists or can be read.

CygNet and WAMI adapters can implement different subsets of these contracts. For example, WAMI is expected to expose calculation capability while CygNet can expose data, alarms, discovery, command, and verification capabilities according to its supported interfaces.

Provider-specific compatibility is evaluated by the adapter/runtime layer and returned through `ProviderCompatibility`. Core domain behavior should reason from semantic capabilities and compatibility state rather than provider product version strings.

## Connection setup and discovery planning

See [Connections, templates, and asset discovery](connections-and-asset-discovery.md) for the 2026-10-19 discussion of named connections, starter templates, customer-defined destination hierarchies, metadata/source bindings, ForeSite WCF investigation, and subsequent logic configuration. This is a planning note; the presence of discovery contracts does not establish an implemented discovery/import workflow.
