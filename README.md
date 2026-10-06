# WellSite AutoPilot

WellSite AutoPilot is a CygNet-adjacent autonomous control and optimization platform for industrial production operations.

This repository is the source of truth for product requirements, architecture, implementation, tests, deployment tooling, and installer assets.

## Current status

Architecture and requirements baseline are being established before feature implementation.

## Planned V1 runtime

- WellSite AutoPilot Server
- Integration Gateway
- .NET Logic Worker
- Python Logic Worker
- PostgreSQL
- NATS + JetStream
- Optional Provider Simulator for development/test

## Core principles

- Safety and operator authority over optimization
- Provider-neutral Logic Modules
- CygNet remains authoritative for SCADA/process telemetry
- PostgreSQL is authoritative for WellSite AutoPilot application state and audit
- Logic Modules emit ControlIntent; the platform governs physical ControlAction
- Offline-capable on-premises operation
- Strong auditability, observability, and deterministic recovery
- Responsive/adaptive engineering UI
- Installer and upgrade validation from the beginning

## Branching

- `main`: stable/releasable
- `develop`: integration branch
- `feature/*`: implementation branches targeting `develop`

