# Connections: workflow, permissions and asset import design

Updated: 2026-10-20.
Status: **proposal for owner review.** Nothing here is implemented. It builds on the owner-agreed simple-first
direction in PR #205 (`docs/architecture/connections-and-asset-discovery.md` and
`docs/development/connections-implementation-plan.md`) and on the findings of the `poc/connections-205` branch
(`docs/poc/connections-205-findings.md`). Where this document and #205 differ, this one records the later
conversation of 2026-10-20 and should be reconciled when both are reviewed.

## 1. Outcome

An engineer connects AutoPilot to ForeSite or CygNet, sees what the provider holds, chooses how it should be
organised, and gets Asset types and an Asset hierarchy created for them, without hand-building either. The same
framework (permissions, grants, audit) governs every step. The first connection feature is read-only.

## 2. Decisions

| # | Decision | Status |
| --- | --- | --- |
| D1 | Administration is split into **Access** (users, roles, grants), **Connections** (providers and imports) and **Status** (platform services and provider health). Service health leaves the Runs page. | Proposed; owner agreed Service health is misplaced |
| D2 | Connections have their **own permission set**, because engineers, not only administrators, work with them. | Agreed |
| D3 | A scoped asset manager may import where they already hold the asset authority. Import adds no power of its own. | Agreed |
| D4 | Engineers may create **asset types**. Asset types become a deployment-wide permission, not an administrator-only fence. | Agreed |
| D5 | Administrators may **hide** asset types that clutter the catalog; anyone who may create types may **unhide** one. A type that was never used may be deleted by whoever may create types. | Agreed in principle; unhide rule proposed |
| D6 | Imported Assets use **neutral types** (for example Well and Group). Provider facts live in a separate **source binding**, not in attributes. | Proposed |
| D7 | Adapters declare their **templates in code** (asset types, groupings). There is no separate "enable template" step. | Proposed |
| D8 | Import runs as **one server-side operation authorized at its attachment point**, not as one create call per node. | Proposed |
| D9 | The project is early: forward migrations are written, but where compatibility work would be awkward the development database may be reset instead. | Agreed |

## 3. Administration layout

Left menu, "Administer" group:

| Item | Contains | Opens with |
| --- | --- | --- |
| Access | Users, Roles, Grants | `security.manage` |
| Connections | Connection list and detail, test, import | `connections.read` |
| Status | Platform services (the health block now on Runs) and provider/connection health; Activity (audit) stays under Access until the audit slice 2 decision | `connections.read` or `security.manage` |

Runs keeps a compact one-line health indicator for operators. `/api/v1/system/info` currently has no permission
check beyond being signed in; it should be gated as part of this move. The "Data sources" name in the UX plan is
retired: Connections is the setup, and bindings appear in Logic and Assets.

## 4. Permissions

The framework separates *what* (a permission), *where* (a scope: deployment, all assets, exact assets) and *who*
(a role granted to a user or group). New permissions follow it.

| Permission | Scope kind | Purpose |
| --- | --- | --- |
| `connections.read` | Deployment-wide | See connection names, provider, enabled state and health in words. No endpoint or settings. |
| `connections.manage` | Deployment-wide, marked privileged | Create, edit, disable and delete connections; test; discover; start an import. |
| `asset-types.manage` | Deployment-wide | Create asset types (including from templates); delete a type that has never been used; unhide a hidden type. |
| `asset-types.hide` | Deployment-wide | Hide a type, with an optional short note. Intended for administrators. |

Rules that change or are made explicit:

- Creating a **root** Asset needs `assets.manage` with the all-assets scope. The separate "protected administrator"
  prerequisite is removed for asset operations and kept for security administration only.
- Creating a **type** needs `asset-types.manage` instead of protected administrator plus all-assets `assets.manage`.
- **Import** needs `connections.manage` plus the existing asset authority: `assets.manage` on the destination
  parent, or all-assets `assets.manage` for root groups, and `asset-types.manage` when a needed type does not exist.
- Control actions (writes to providers) are **not** part of `connections.manage`. They get a separate permission
  when write support is designed.
- A future per-connection scope is deferred; it would be a new resource kind.

Each endpoint is registered in the API authorization CI check. The seeded Administrator role must be migrated to
hold every new permission, because bootstrap verifies the role holds exactly all of them. The UI follows server
hints (`/security/me`, per-resource capability flags) and the server re-authorizes every request.

Example roles: **Engineer** = connections, asset-types.manage, assets.manage on its scope, logic and
configured-logic management. **Operator** = read and run permissions only. **Administrator** = everything,
including `asset-types.hide` and `security.manage`.

## 5. Workflow

1. **Add connection.** Choose a provider (a short description of each), name it, fill the provider's fields.
   ForeSite: address. CygNet: agent address, domain, site, service and facility type (default WELL).
2. **Test.** Check reachability, authentication and the ability to list, with a result in words and a next step.
   The result is tied to the tested revision; a later edit makes it stale.
3. **Find.** Discover what the provider holds ("found 38 facilities"), with search and selection.
4. **Structure.** Choose the grouping: provider presets, reorderable levels, or "mirror the provider's hierarchy".
   A live tree preview shows counts and fallback groups ("Unassigned").
5. **Review.** Show what will be created (types, groups, Assets), conflicts, and what already exists. Nothing is
   written before Apply.
6. **Done.** Show what was created with a link into Assets. A later "check for changes" lists new, renamed and
   missing items. Nothing is deleted automatically.

Empty Assets page: offer "Import from a connection" next to adding an asset by hand. The Asset types screen offers
"start from a template" from the same catalogue.

CygNet specifics: domain, site and service are entered on the connection (as proven in the POC). A later agent call
that lists domains, sites and services can turn them into pickers. Discovery in the first slice shows what the
service identity can see and says so; showing the signed-in person's own CygNet rights needs Kerberos delegation and
belongs to the unresolved CygNet attribution requirement.

## 6. Data model

- **Connection** (PostgreSQL, owned by the Server): id, name, adapter key, endpoint, typed settings, enabled flag,
  revision, last test result tied to its revision, audit. Protected secrets are not stored in the first slice.
  Endpoint or adapter replacement is rejected once a connection is referenced; deletion is blocked while referenced.
- **SourceBinding**: asset id, connection id, resource kind, external id, snapshot of discovered metadata, first and
  last seen, present or missing. Unique on connection plus resource kind plus external id. An Asset may have several
  bindings: a ForeSite well and the CygNet facility behind it are the same physical well.
- **Asset types**: neutral Well and Group types with a small common schema, shipped as adapter-declared templates.
  Templates are versioned; a changed template creates a new type version and never alters existing types.

## 7. The import operation

`POST connections/{id}/import` receives the reviewed selection, the grouping and the destination parent. The server
re-reads discovery, checks authority once at the attachment point, then creates the whole subtree in one
serializable transaction with the normal type, attribute and schema checks, writes the source bindings, and records
one audit event with counts. It is idempotent per connection plus resource kind plus external id, previews
conflicts instead of overwriting local fields, and does not match Assets by name.

This replaces the POC's per-node create calls. Under the current rules a scoped manager's per-node call would be
refused at the second level, because the new groups have no grant of their own. Visibility of what was created is
section 9.

## 8. Types: hide, unhide and delete

Most clutter is types nobody used (typos, experiments); those are **deleted**, not hidden. Hiding applies to types
that Assets already use.

- **Hide** (`asset-types.hide`) sets the existing inactive flag with an optional note of up to 200 characters
  ("superseded by Well"). A hidden type is not offered in Asset creation pickers or module Asset-type filters, so
  nothing new is built on it. Existing Assets of it keep working. Every change is audited.
- **Visibility:** operators never see hidden types. Holders of `asset-types.manage` (and administrators) can switch
  on "Show hidden"; hidden types are labelled with who hid them, when, and the note. The UI says "Hidden".
- **Unhide** needs `asset-types.manage`. Restoring is harmless and audited, so an engineer who needs a hidden type
  does not wait for an administrator. If administrator-only unhide proves necessary, it can be tightened later.
- **No silent duplicates:** defining a type whose key matches a hidden type returns a specific message ("a hidden
  type with this key exists"), not a generic key conflict.
- **Delete** is allowed only for a type with no Assets, ever, and is safe against a concurrent create.
- The Asset types table already shows a per-type count, which guides decluttering.

## 9. Open questions

1. **Visibility after import.** Grants bind exact Assets with no descendant implication, and creating an Asset does
   not grant the creator access to it. Imported children are invisible to scoped users until granted. Options:
   administrators grant afterwards (status quo plus a link from the result screen); or research a hierarchy-scoped
   grant or creator access as a separate framework decision. Bulk grants, nested creation and import visibility are
   the same root issue.
2. **Whose rights apply to discovery.** First slice: the service identity, labelled as such.
3. **CygNet per-person attribution** and the identity of unattended actions remain open before any write.
4. **Linking** a ForeSite well to its CygNet facility as one Asset with two bindings, instead of duplicating.
5. **Large imports:** bounded preview paging, background apply with progress, ForeSite warm-up of about 90 seconds,
   explicit timeouts.
6. **Gateway boundary:** the existing `/integrations` and `/data/current` routes are unauthenticated and must be
   closed and tested before real provider access.
7. **Warnings channel** for partial results (for example CygNet's read-failed flag) and a paging token in the
   discovery contract.

## 10. Delivery order

1. Framework permissions: add `asset-types.manage` and `asset-types.hide`, relax the root and type fences, add
   hide and delete for types, migrate the seeded Administrator role, update tests and the CI authorization check.
2. Connections: persisted connections, `connections.read` and `connections.manage`, the Connections page, test and
   status, simulator use through an authenticated Gateway.
3. First real adapter productionised from the POC (ForeSite), with its support note.
4. Discover, preview and the import operation with source bindings and templates; then the CygNet adapter on the
   same path.
5. Status page and the Administer layout, gating `/system/info`.
6. Use connections from configured Shadow logic.

## 11. From the POC to production

Reuse the adapter projects, their recorded and live tests, the health semantics, the hierarchy-level model and the
CygNet agent shape. Rewrite the JSON-file connection store (PostgreSQL), the Server endpoints (permissions, import
operation, source bindings), and the UI (two-pane layout, localization, design tokens). Do not merge the POC branch
wholesale.
