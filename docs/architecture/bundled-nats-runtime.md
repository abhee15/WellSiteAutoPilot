# Bundled NATS Runtime

WellSite AutoPilot V1 bundles a pinned NATS Server binary for the local Runtime Node so normal operation does not require Internet access or a separately provisioned message bus.

## Current foundation behavior

- NATS Server version is pinned by CI.
- CI downloads the official Windows AMD64 release archive and verifies its SHA-256 digest before packaging it.
- The same NATS minor version is exercised by the JetStream integration job.
- The installer places the binary under `Infrastructure/NATS`.
- NATS Server itself is not treated as a native Windows Service executable. A small WellSite AutoPilot `NatsHost` Windows Service owns the child `nats-server.exe` process and its lifecycle.
- The bootstrapper registers `Weatherford.WellSiteAutoPilot.Nats` using the WSA `NatsHost` executable.
- NATS listens only on `127.0.0.1:4222`.
- JetStream storage is placed under the WellSite AutoPilot ProgramData tree.
- Mutable JetStream data is preserved when the product is uninstalled.
- CI installs the product, verifies the NATS Windows service, validates JetStream topology/deduplication against the installed service, and then verifies uninstall cleanup.

## Security progression

Loopback-only binding is the minimum foundation boundary. Production hardening still requires NATS authentication/credential generation and subject-level authorization before autonomous control capability is enabled. These credentials must be installer/bootstrapper-managed and must not be committed to source control or written into ordinary application settings.
