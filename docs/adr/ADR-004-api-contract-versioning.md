# ADR-004: Explicit Major API Contract Versioning

## Status

Accepted.

## Decision

WellSite AutoPilot HTTP APIs use explicit major versions in their URL paths.

- Product API: `/api/v{major}/...`
- Internal Integration Gateway API: `/api/internal/v{major}/...`
- V1 routes therefore resolve to `/api/v1/...` and `/api/internal/v1/...`.

ASP.NET API Versioning supplies version semantics and API Explorer metadata. URL-segment versioning is the authoritative version reader.

## Compatibility policy

Backward-compatible additions remain in the current major version. A new major version is required when a change would break an existing client or materially alter the established semantics of an existing contract.

Examples that normally remain in V1:
- adding an optional response field;
- adding a new endpoint;
- adding an optional request field with backward-compatible behavior.

Examples that require a new major version:
- removing or renaming a field used by clients;
- changing an identifier or field type incompatibly;
- changing required request structure;
- changing the established semantics of an endpoint incompatibly.

## OpenAPI and generated clients

OpenAPI contracts and generated TypeScript clients are version-specific.

```text
artifacts/openapi/v1/server.json
src/web/src/api/generated/v1/
```

Generated transport DTOs are never hand-edited. Frontend view/workflow models remain separate from generated API transport models.

When V2 is introduced, V1 and V2 can coexist during the supported compatibility window.

## Independent version domains

The following versions evolve independently and must not be treated as one shared version number:

- WellSite AutoPilot product version
- REST API version
- internal Integration Gateway API version
- NATS message contract version
- database migration/schema version
- ACL SDK version
- Logic Module manifest/package version
- CygNet adapter version
- WAMI adapter version

## Rationale

On-premises industrial deployments and field upgrades can leave multiple product/client versions active for meaningful periods. Explicit major contracts prevent ordinary product releases from accidentally becoming API-breaking releases and provide a controlled path for compatibility during upgrades.
