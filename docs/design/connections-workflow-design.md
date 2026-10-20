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
| D10 | **One physical object is one Asset.** A ForeSite well is one Asset whether or not it is connected to CygNet. ForeSite is the system of record for wells; a CygNet facility attaches as an additional source binding (proposed automatically from the ForeSite facility id, confirmed by the engineer). A CygNet-only well is its own Asset and can later be linked to a ForeSite Asset instead of duplicated. | Agreed |
| D11 | The engineer owns hierarchy design. AutoPilot previews, flags conflicts and already-imported objects and limits parents to those the engineer may manage, but does not police the grouping. | Agreed |
| D12 | Several arrangements of the same source are saved **Views** (named, ordered groupings computed from Asset attributes and binding data), not duplicate Assets and not multiple parents. | Proposed |
| D13 | Source changes are reconciled by an explicit **check for changes**: new, changed and missing. Nothing is deleted automatically, and only a complete, successful check may report objects as missing. | Proposed |
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

## 7a. Identity, views and keeping in step with the source

**Identity.** The binding key is connection plus resource kind plus external id, unique. A second import of the same
object is reported as "already imported" and offers a View, never a second Asset. Linking a second source to an
existing Asset adds a binding.

**Views (proposed).** A View is a saved, named set of filters and an optional ordered grouping over Asset attributes and
binding data, for example Field > Lease > Well, or a flat list of all wells with well type ESP wherever they sit in the
main tree. A View shows only the Assets the viewer may read, so different people can see different results from the
same View. A filter works only when the attribute exists (a CygNet-only well has no well type unless it is mapped).
Views are stored on the server, not in the browser as today's saved filters are, so they can be shared and used by
logic setup. Proposed rule: personal Views are private; a shared View is editable by its creator and by holders of
`assets.manage` on the Assets involved. It is computed when displayed: nothing is duplicated, no
group Assets are created, and it follows source changes by itself. The real tree (parent links) stays the structure
for containment and grants. The View builder is the same level builder used in the import's Structure step. If
modules need to select a whole group, logic setup can expand a group or View into explicit Asset bindings; real
group Assets that carry their own logic are a later option.

**Check for changes (proposed).**
- Compare a complete discovery with the saved bindings: *New* (not bound), *Changed* (source data differs from the
  saved snapshot), *Missing* (bound but no longer returned).
- A partial or failed discovery (warnings, paging failure, cold start, lost permissions) is inconclusive and never
  produces "missing".
- A missing object marks the Asset "source missing since..."; the engineer chooses keep, hide or detach. Asset
  hide/deactivate is a new action (reusing the inactive flag, audited), to be designed with the same pattern as types.
- Ownership after import: name and position in the tree belong to the Asset; attributes mapped from the source are
  refreshed and labelled as coming from the source; the template type allows local-only attributes.
- Checking is manual at first and produces a review list. Scheduled checks may later propose changes but do not
  apply them.

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

0. Import, Views, change checking and the Assets page changes (source section, views selector, needs-attention filter) follow in that order after the connection and ForeSite steps below.
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

## 12. Slice 2 in detail: Connections

Written because section 6 names the entity but not how it behaves. The POC is evidence of what works against real
systems, not a design; nothing below is taken from it unless stated. Items marked **Decide** need an answer before
code.

### 12.1 What a connection is

A named, enabled-or-disabled link from this deployment to one external system, described by an adapter key and a
small set of settings that the adapter declares. It carries no discovered data and no secrets. Everything that
follows (discovery, import, source bindings) refers to it by id.

| Field | Rule |
| --- | --- |
| Name | Unique, 1 to 100 characters, shown everywhere. |
| Adapter | One of the adapters this build ships: `simulator` first, then `foresite`, then `cygnet`. Fixed after the connection is first used. |
| Settings | Adapter-declared typed fields, stored as validated JSON. ForeSite: base address. CygNet: agent address, domain, site, service. Validated against the adapter's declaration on save. |
| Enabled | A disabled connection keeps its bindings but cannot be tested, discovered or imported. |
| Revision | Increments on every change; edits send the revision they read, and a stale edit is refused. |
| Last test | Outcome, message, time and the revision it was run against. A result for an older revision is shown as "out of date". |

**Decide 1: where adapters declare their settings.** Recommended: the Server ships a small catalogue of adapter
declarations (key, display name, settings fields with labels, help text and validation), shared with the UI through
the API. The Gateway only executes. This keeps validation and the form in one place and means a new adapter is a
code change, not a database change. Alternative: the Gateway publishes declarations at startup, which adds a
dependency on the Gateway being up just to render a form.

### 12.2 Permissions and endpoints

`connections.read` lists and opens connections and shows last test results. `connections.manage` creates, edits,
enables, disables, deletes and tests. Both are deployment-wide, as section 4 says. Engineers get both; operators
get neither; the Admin role gets both because it must hold every permission.

| Endpoint | Permission |
| --- | --- |
| `GET /connections`, `GET /connections/{id}` | `connections.read` |
| `GET /connections/adapters` | `connections.read` |
| `POST /connections` | `connections.manage` |
| `PUT /connections/{id}` (carries expected revision) | `connections.manage` |
| `PUT /connections/{id}/enabled` | `connections.manage` |
| `POST /connections/{id}/test` | `connections.manage` |
| `DELETE /connections/{id}` | `connections.manage`; refused while any source binding refers to it |

Every change writes an audit event with the actor and the setting names that changed (not values that might later
hold something sensitive). The CI authorization check lists each endpoint.

### 12.3 Test and status

A test is a bounded, read-only call through the Gateway (default timeout 10 seconds) that answers three things:
reachable, accepted our identity, and what the system said about itself. Outcomes:

- **Healthy**: the system answered as expected.
- **Needs attention**: the system answered but refused our identity or reported a problem (for example 401 or 403).
- **Unreachable**: no answer in the time allowed.

The result is stored against the revision. Testing never changes data in the external system. There is no
background polling in this slice; a scheduled check belongs with the Status page (delivery step 5).

### 12.4 How the Server reaches adapters

The browser never talks to the Gateway. The Server calls the Gateway with a service credential and the Gateway
rejects anything without it, which also closes the open `/integrations` and `/data/current` routes from section 9.
The Gateway selects the adapter from the connection's adapter key and settings, which the Server sends with each
call; the Gateway stores no connections of its own. **Decide 2:** the credential. Recommended: a shared key from
configuration, supplied by the installer, rotated by reinstalling or editing configuration; mutual TLS is stronger
but heavier to install on the single-server deployments we target.

### 12.5 The Connections page

Under Administer, shown when the person holds `connections.read` (managing controls need `connections.manage`).
Two-pane like Assets: a list on the left (name, adapter, state, last test), details on the right with the settings,
last test result, a Test button and, for managers, Edit, Disable and Delete. Creating starts with choosing an
adapter, then the declared fields. The page says plainly that discovery uses the service identity until per-person
access is decided (section 9, question 2). Strings go through localization and styles through the design tokens,
as everywhere else.

### 12.6 What this slice deliberately leaves out

Secrets storage, discovery and import (step 4), ForeSite and CygNet adapters beyond reachability (steps 3 and 4),
scheduled checks and the Status page (step 5), and any change to Assets.

### 12.7 Tests

Persistence: unique name, stale-revision refusal, delete refused when referenced (once bindings exist; the rule is
written now and tested with a fixture row), audit on every change. API: each endpoint allowed and denied per
permission, settings validated per adapter, test result tied to revision. Web: list, create, edit, stale edit
message, test result states, permission gating. Gateway: rejects unauthenticated calls and still serves the
simulator.
