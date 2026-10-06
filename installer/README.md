# Installer

V1 installer architecture:

```text
NSIS
  -> WellSite AutoPilot .NET Bootstrapper
      -> preflight
      -> machine/runtime configuration
      -> PostgreSQL setup/connectivity
      -> EF database migrator
      -> NATS configuration
      -> Windows service registration
      -> certificate/firewall configuration
      -> compatibility validation
      -> health validation
```

The installer pipeline will be introduced with the first runnable application skeleton so every useful development build can produce a downloadable Windows installer artifact.
