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

## Connections and delivery scope

See [Connections, templates, and asset discovery](connections-and-asset-discovery.md) for the owner-agreed simple-first scope and the [implementation plan](../development/connections-implementation-plan.md) for delivery slices after authentication validation.

Initially maintain separate provider adapter projects in this repository, explicitly register them in the Gateway, and test/release supported adapters with AutoPilot. Provider software itself remains independently installed and upgraded. Connections are runtime configuration; no installer changes or general plugin distribution framework are required.

ForeSite/WAMI versioned contracts or a product-version endpoint are not assumed. Start with documented tested support profiles and targeted non-mutating checks, with clear unknown/unsupported outcomes. Extend existing semantic contracts only for demonstrated adapter needs. A public SDK and external hosting remain future options after real adapter experience and a concrete requirement; no current interface is claimed as a finished external protocol.
