# Agreed development follow-ups

Record user observations and agreed decisions without interrupting the active
priority. Observations and questions are discussion/planning input unless the
user explicitly changes priority. Keep unresolved ideas distinct from agreed
implementation decisions; resolve them against the roadmap when selecting
subsequent work. Do not lose them when a feature or session completes.

## API enum generation — 2026-10-16

The user raised client-side enum declarations during direct-administrator
recovery work and explicitly deferred action until that task is finished.
API-owned finite values should flow from the server contract through OpenAPI
into generated TypeScript types, rather than independently maintained client
unions. Frontend-only tabs, filters and interaction state can remain local.

Verified gap: `AccessGrantResponse.SubjectKind`, `ScopeKind` and
`CreateAccessGrantRequest.ScopeKind` are C# strings; the generated client
therefore emits strings, and `scopedAccessGrants.ts` manually narrows them.
Audit other finite API fields (execution mode/status, recommendation decisions,
and configuration status) before choosing the bounded follow-up. Some string
fields, such as customer role names and binding roles, are intentionally open.

Acceptance: typed finite contract values represented in OpenAPI and generated
TypeScript; client consumers import those generated types; preserve current
wire names and fail-closed validation; test unknown/malformed/numeric values
and regeneration. Never edit generated files by hand. Runtime response
validation remains necessary even with generated compile-time types.

### Roadmap step 1b result — 2026-10-18

Converted to typed, closed contract values (JSON strings unchanged, OpenAPI
string enums, generated TypeScript unions): `AccessGrantResponse.SubjectKind`
and `ScopeKind`, `CreateAccessGrantRequest.ScopeKind`, execution
`Trigger`/`Mode`/`Status` (and the shadow receipt), recommendation `Status`,
the decision request (`RecommendationDecision`: `Approved`|`Rejected`),
configured-logic revision `Mode`/`Status`, module `Runtime`/`ExecutionProfile`/
`TrustStatus` and platform component `Status`. Mechanism: contract enums in
`Api.Contracts` with `ClosedStringEnumConverter<T>` (exact, case-sensitive
names; numbers, numeric strings and undefined members rejected; member `0` is
undefined so an omitted property never becomes a value), mapped to the domain
enums by name in `Server/Contracts/ContractEnumMapping.cs`. Intentionally left
as strings: role names/keys, binding roles, asset type keys, result/failure
codes, and the query-string `status` filters (parsed server-side, still
case-insensitive). `RolePermissionDefinitionResponse.ScopeKind`
(`ResourceScoped`|`DeploymentWide`) is closed but has no enum in the
application or persistence layers (it is compared as a string in many places);
converting it needs its own change.

PR #127 completed the coordinated enterprise RBAC runtime integration and
merged as d23ef0d after seven passing checks. The next agreed Phase 1 priority
is the real Windows browser diagnostic under #92. Enum generation remains
queued and does not interrupt that investigation.

## Connections and asset discovery — agreed simple-first scope, 2026-10-20

The owner agreed that Connections is the next foundation after authentication is completed and validated. Consolidated [design](../architecture/connections-and-asset-discovery.md) and [high-level implementation plan](connections-implementation-plan.md) supersede earlier broad installer/plugin proposals.

- Connections are runtime configuration for independently installed providers.
- Maintain each adapter in its own project inside the AutoPilot repository; explicitly register, test and release supported adapters with AutoPilot initially.
- Deliver cohesive slices: saved connections and authorized simulator use; real read-only ForeSite validation; discovery/preview/apply; configured Shadow logic usage; then additional concrete capabilities/providers.
- Use documented tested support profiles and targeted checks. Do not assume ForeSite/WAMI versioned contracts, automatic release detection or a compatibility graph.
- Retain protected configuration, credentials, stable IDs, concurrency/audit, exact-Asset and Gateway authorization, and simple referenced-connection protection. No general dependency or plugin engine is required.
- Keep the boundary extensible, but defer a public SDK, independent adapter distribution and external hosting until real adapter experience and a concrete need justify them.
- CygNet per-person attribution/enforcement and unattended execution identity remain unresolved before writes; read-only Connections do not silently settle that requirement.

PR #205 remains draft and unmerged until explicit owner approval. This documentation plan does not interrupt authentication work or claim implementation.
