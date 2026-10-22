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
| D12 | A **View** is a named explicit subset of wells from one Connection, selected through the imported hierarchy; membership does not duplicate Assets or alter parents. | Proposed |
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

**Views.** A View stores an explicit selected set of wells from exactly one Connection. Users browse the
imported hierarchy, select wells across branches, and review the saved membership. Connection read access
controls visibility; separate create/edit/delete permissions control mutations, independently of creator.
Assignments store immutable target bindings and the reviewed View revision. See sections 18 and 19.

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
3. Installer support for a configurable, upgrade-preserved Gateway service account (section 12.5a), then the first real
   adapter productionised from the POC (ForeSite), with its support note.
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
systems, not a design; nothing below is taken from it unless stated. The three decisions below were researched against the
repository and are recorded with their reasons; challenge them here before code.

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

**Decision 1 (researched): adapters declare their own settings; the Server stays provider-neutral.** An earlier
draft of this section proposed that the Server ship the declarations. That was wrong: ADR-005 keeps provider
knowledge inside adapters and the core provider-neutral, and a Server catalogue would put ForeSite and CygNet
fields into the core and into web code. Instead:

- Each adapter project owns one `AdapterDescriptor`: key, display name, description, a `SettingsVersion`, its
  capabilities (the existing `ProviderCapability` list) and a flat list of setting fields. A field is key, label,
  help text, kind (text, URL, whole number, yes/no), required, and maximum length. No conditional fields, no
  nesting, no schema language. This is the small shared form component the owner-agreed direction allows, not a
  schema-driven form engine or manifest format.
- The Gateway registers adapters in code and serves `GET /adapters` (descriptors) and
  `POST /adapters/{key}/validate` (settings in, field problems out). Adapters may check more than types, for
  example that an address is a well-formed `http` or `https` URL.
- The Server stores `Settings` as opaque JSON beside the adapter key and the settings version. It never reads a
  provider field. It refuses to save settings the adapter rejects; if the Gateway cannot be reached the save fails
  with a clear message rather than storing unvalidated settings.
- The web page renders whatever descriptor the Server relays. Adding an adapter touches the adapter project and the
  Gateway registration only. Labels and help from an adapter are data supplied in English; they are not part of the
  UI localization catalogue until a second language is a requirement.
- `SettingsVersion` is a cheap hook: when an adapter changes its fields, a connection stored at an older version is
  shown as "needs review" and must be re-saved. A migration framework is deliberately not built.

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

### 12.4 How callers reach the Gateway (decision 2, researched)

**Facts that shape it.** The browser never talks to the Gateway. Two components call it: the Server (connection
management, test, later discovery and import) and the .NET worker (current data for executions, later). The
installer runs the Gateway, worker and NATS as LocalService and the Server as NetworkService, all on loopback
except the Server. LocalService presents anonymous credentials on the network, so Windows (Negotiate) sign-in cannot
identify the worker or the Gateway; NetworkService and virtual accounts all appear as the machine account, so
Windows sign-in would not tell the Server from any other NetworkService process. The architecture baseline already
says service-to-service calls use a service identity plus grants.

**What must be true.** (1) An unauthenticated or unknown caller is refused. (2) The two callers are different
callers with different rights: the worker runs module code and must never reach connection management, test or
discovery. (3) No secret is shared more widely than it needs to be, and none is on a command line, in source or in
ordinary application settings (the same rule already applied to the database password and to NATS credentials).
(4) It works unchanged on the single-server installs we target, in local development and in CI, without Active
Directory configuration.

**Design: one key per caller, verified by hash.**

- Each caller has a name (`server`, `worker`) and a random 256-bit key generated by the installer.
- A caller sends `Authorization: WSA-Service <name>:<key>` on every call. The Gateway holds only the SHA-256
  verifier of each key (the keys are high entropy, so a plain hash is sufficient) and compares in constant time.
- Where things are stored: the Server's key in the existing protected settings file, readable by the Server's
  account only; the worker's key in its own file readable by LocalService; the verifiers in a Gateway file readable
  by LocalService. The worker's account therefore cannot read the Server's key, and neither can learn the other's
  secret from the verifiers. This is the isolation the two-caller split buys.
- The Gateway maps the caller name to a role and authorizes each route by role. `server`: everything internal.
  `worker`: the current-data route only, and later only for bindings that resolve to an enabled connection.
  Anything else is 403, and both outcomes are logged without echoing the key.
- Install and upgrade keep existing keys; an explicit rotate option regenerates them and restarts the services.
- Development and tests supply the keys through configuration (Aspire parameters locally), so no test depends on
  Windows.
- The Gateway refuses to listen on a non-loopback address unless HTTPS is configured, because the key is sent in
  every request.
- The authentication scheme is a pluggable ASP.NET handler that only produces a caller name. If a deployment later
  runs per-service domain accounts, a Negotiate or mutual-TLS handler can produce the same caller names without
  changing any authorization rule.

**Alternatives considered and why not now.**

| Option | Why not the first choice |
| --- | --- |
| Windows (Negotiate) with an allowed-account list | LocalService cannot authenticate; NetworkService and virtual accounts collapse to the machine account, so it identifies the machine, not the service. Needs per-service domain accounts and IT agreement to be meaningful. Kept as a later handler. |
| Named pipe with an access list | Operating-system enforced and secret-free, but Windows-only security, so CI and Aspire on Linux cannot exercise it, and it rules out a Gateway on another host. |
| Mutual TLS | Certificate issuance, renewal and trust for three loopback services is more installer and support burden than the threat justifies. |
| One shared key for all callers | Simplest, but the worker could then call connection management. Rejected because the worker runs module code. |

**What this does not protect against.** A process running as the same account as a caller can read that caller's
key, and a local administrator can read everything. On a single host those are inside the trust boundary; the
protection is against the network, other accounts, and the worker reaching the Server's privileges.

**Looking ahead (step 4).** When a worker asks for current data it must name a connection and a binding, not supply
settings. The Gateway therefore needs an authoritative way to obtain a connection's settings by id. Recommended: a
narrow Server endpoint the Gateway calls with the `server`-equivalent internal key, cached by connection revision,
so the Gateway still stores nothing of its own. This is recorded now so slice 2 does not paint it into a corner;
it is built in step 4. In slice 2 only the Server calls the Gateway, and it sends the settings itself.

### 12.5 The Connections page

Under Administer, shown when the person holds `connections.read` (managing controls need `connections.manage`).
Two-pane like Assets: a list on the left (name, adapter, state, last test), details on the right with the settings,
last test result, a Test button and, for managers, Edit, Disable and Delete. Creating starts with choosing an
adapter, then the declared fields. The page says plainly that discovery uses the service identity until per-person
access is decided (section 9, question 2). Strings go through localization and styles through the design tokens,
as everywhere else.

### 12.5a Identity the Gateway uses toward providers (decision 3)

The Gateway's own Windows account is the identity ForeSite and CygNet see. Under LocalService that identity is
anonymous on the network, so real Windows-authenticated provider calls will fail. The installer today deletes and
recreates every service with a fixed account on each run, so an account an administrator sets by hand would be lost
at the next install or upgrade.

Therefore, before the first real adapter (step 3), the installer needs a way to run the Gateway under a chosen
account (a domain or group-managed service account) and to keep it on upgrade. This is a small separate slice, not
part of Connections itself, and it matches the research note that service accounts must become configurable.
For slice 2 nothing changes: the simulator needs no provider identity.

**Decided and built (step 3).** The installer takes `--gateway-account DOMAIN\name` (or `.\name`; silent
`/GATEWAYACCOUNT=`) and `--gateway-account-default`. A group-managed service account (name ends in `$`) is the
recommended choice because Windows holds its password; an ordinary account's password arrives through
`WSA_GATEWAY_ACCOUNT_PASSWORD` and is never stored or printed, so an upgrade must supply it again and is refused
with a clear message if it does not. The chosen account is remembered in the machine settings (`Installer:GatewayAccount`),
only after the Gateway has started under it, and an upgrade that does not mention it keeps it. The installer grants
the account the log-on-as-a-service right through the local security policy and makes `gateway.keys.json` readable
by that account alone. Only the Gateway can be changed this way; the Server, worker and NATS keep their fixed accounts.

The test result states which identity the Gateway used ("acting as DOMAIN\account"), read from the running process,
so a permission problem is diagnosable from the page. Connection settings can never select or change the account.
Per-person CygNet attribution remains the open requirement in section 9.

### 12.6 What this slice deliberately leaves out

Secrets storage (the caller keys in 12.4 are infrastructure, not connection data), discovery and import (step 4), ForeSite and CygNet adapters beyond reachability (steps 3 and 4),
scheduled checks and the Status page (step 5), and any change to Assets.

### 12.7 Tests

Persistence: unique name, stale-revision refusal, delete refused when referenced (once bindings exist; the rule is
written now and tested with a fixture row), audit on every change. API: each endpoint allowed and denied per
permission, settings validated per adapter, test result tied to revision. Web: list, create, edit, stale edit
message, test result states, permission gating. Gateway: rejects unauthenticated calls and still serves the
simulator.

## 13. Step 4 in detail: find, structure and import

Written after steps 1 to 3 shipped, to settle the open questions in section 9 that block building. The POC is
evidence of what ForeSite returns, not a design.

### 13.1 Decisions

| # | Decision | Reason |
| --- | --- | --- |
| 13.1a | **Who may import (answers 9.1).** Import needs `connections.manage`, **all-assets** `assets.manage`, and `asset-types.manage` when a needed type does not exist. A scoped manager is refused with a plain message. | Grants bind exact Assets and creating an Asset grants the creator nothing, so a scoped manager would create children they cannot see. Refusing is honest and safe; importing under a parent for scoped managers waits for a hierarchy-scoped grant, which is a separate framework decision. Section 4's rule is narrowed accordingly. |
| 13.1b | **Discovery identity (9.2).** The Gateway's service account, stated on the screen ("Found as DOMAIN\account"). | Per-person identity needs delegation and is the open CygNet requirement. |
| 13.1c | **Discovery contract (9.7).** `DiscoveryResult { complete, items, warnings }`. `complete` is false for any partial answer; a check for changes may only report "missing" from a complete result. No paging token yet: ForeSite returns everything in one answer, and the contract can gain one when an adapter needs it. | Matches the POC finding; keeps the "missing" rule enforceable. |
| 13.1d | **Size (9.5).** A discovery is capped at 5,000 objects; larger results are refused with a message to narrow the source. Preview is computed by the Server from one fresh discovery; apply re-reads, re-plans and refuses if the plan changed since the preview (a plan fingerprint is compared). No background job in this step. | Bounded work in one request and one transaction; the re-plan prevents applying a stale review. |
| 13.1e | **Neutral types.** `well` and `group`, created on first import when absent. If the type key exists, it is used as is and the plan reports a conflict when its schema rejects the values. The sample logic module already requires `well`. | Keeps imported Assets usable by existing modules and avoids a type per provider. |
| 13.1f | **Attributes and bindings.** Mapped facts that people filter on (`field`, `lease`, `wellType`, `deviceType`) are Asset attributes; everything else the provider said is kept in the binding's snapshot. | D6 plus the Views requirement to filter on attributes. |
| 13.1g | **Grouping.** An ordered list of levels chosen from the adapter's declared grouping fields (ForeSite: `assetName`, `wellType`, `deviceType`, `field`, `lease`), always ending in the object itself. A missing value falls back to a labelled group (`Unassigned`, `Unknown type`, `Unspecified device`). Group names need not be unique across parents, and a group is matched by parent plus name within one import. | From the POC findings; the engineer owns the design (D11). |
| 13.1h | **Idempotence.** The binding key is connection + resource kind + external id. Already-bound objects are reported "already imported" and skipped; existing groups (by parent + name + type `group`) are reused, never duplicated. Nothing is matched by Asset name alone. | D10 and section 7a. |

### 13.2 Slices

1. **4a Find.** Adapters gain `discover`. Gateway `POST /api/internal/v1/adapters/{key}/discover` (server caller only)
   returns a `DiscoveryResult`; the ForeSite adapter reads `Wells/All` and the asset names. The Server adds
   `POST /connections/{id}/discover` (`connections.manage`), and the Connections page gains "Find" showing the
   count, warnings, the identity used and a searchable list. Nothing is saved.
2. **4b Plan.** A pure planner turns a discovery and a chosen level list into a tree with counts, fallback groups
   and conflicts; `POST /connections/{id}/import/preview` returns it with a fingerprint. Still nothing saved.
3. **4c Import.** `source_bindings` table, the types, and `POST /connections/{id}/import` applying a re-planned,
   fingerprint-checked plan in one serializable transaction with one audit event carrying counts. Deleting a
   connection becomes blocked while bindings reference it (the rule written in 12.7).
4. **4d Page.** Structure (level builder with live preview), review and result in the Connections page; the
   empty Assets page offers "Import from a connection".
5. Then the CygNet adapter on the same path, linking (9.4), and check for changes (7a) as later slices.


### 13.3 Built (step 4)

Slices 4a to 4d shipped as #217 to the import screens PR. As built:

- Find, plan and import all start from a fresh discovery. A plan needs a complete discovery; an incomplete one is
  refused with the adapter's reason rather than shown as "nothing there".
- Adapters declare grouping facts with a label for objects that lack the value and whether the fact is also kept as
  an Asset attribute (`DiscoveryField`). ForeSite's asset name only shapes the tree.
- Preview and import are separate calls. Import repeats the plan and refuses if its fingerprint is not the one
  reviewed. Objects already bound are skipped, so a second import only adds what is new.
- Groups are not bound; they are matched by parent, name and type on each import. Hand-made groups with the same
  parent and name are therefore reused.
- Deleting a connection is refused while it has imports. Disabling it is the way to stop using it.
- The Connections page shows the importer for an enabled, saved connection whose adapter can discover. The empty
  Assets page links to it for people who can manage connections.

Known limits, to revisit: a bound Asset that is later deactivated still counts as imported; scoped managers cannot
import (13.1a); no check for changes yet.

## 14. Status page and gating `/system/info` (step 5)

`/system/info` lists internal component names and versions, so it is no longer open to every signed-in person.

- **Split.** `GET /system/info` (names, versions, state) requires `system-status.read`, an any-of policy satisfied by `connections.read` or `security.manage`. A new `GET /system/health` returns only an overall state and counts (`status`, `componentCount`, `unhealthyCount`) to any signed-in person, so operators keep the health indicator on Runs and Overview without seeing component names.
- **Status page** (Administer group, opens for the same two permissions). Two independent parts: platform services from `/system/info`, and each connection's last test result from `/connections`. A part the person cannot see is not requested; a part that fails to load says so rather than appearing empty. It refreshes every 30 s and on demand.
- **Not built yet.** Scheduled connection checks. A connection's state only reflects the last manual test, which the page states as "Last tested …" or "Never tested". Background checks need a system actor, a frequency and audit-on-change rules, and get their own design first.

## 15. Systems that hold more than one import can handle

**Observed.** A real ForeSite server holds more wells than the 5,000-object ceiling (13.3). Find and Review both stop with
"holds more than one import can handle", and the advice in that message ("narrow what the connection's account can see")
is not something the person can act on.

**Why there is a ceiling at all.** Discovery, the plan and the preview run in one request; the browser receives the whole
list; Import re-reads the system and writes everything in one serializable transaction. The cost is in the transfer and the
single transaction, not in storing what was found.

**Rejected.**
- *Show the first 1,000 and import those.* A cut-off list reads as a complete one, and nobody can tell which wells were left
  out. A partial answer must never look like everything (the rule already used for incomplete discovery).
- *Import the found objects into the system so they can be reviewed.* Importing creates Assets. Review needs the list held
  briefly, not created.

**Decided.**
1. **Raise the limit on what may be found** to 50,000 objects and put the real number in the message when it is exceeded.
2. **Find returns a summary and a sample, not the whole list.** The total, the count per value of each grouping field, and
   the first 1,000 objects for browsing and searching. The browser never receives tens of thousands of rows.
3. **Scope before import.** The person narrows by grouping-field values and by search text. Review and Import work on that
   selection. The 5,000 ceiling then applies to what is imported at once; imports are repeatable (bindings make them
   idempotent), so a large system goes in by field or lease.
4. **A short-lived snapshot.** The Server holds what discovery returned, in memory, keyed by connection and person, for about
   15 minutes. Browsing, filtering, Review and Import read the snapshot, so what was reviewed is exactly what is imported and
   the system is not re-read (ForeSite can take over a minute) for every change of filter. An expired or lost snapshot (for
   example after a restart) says "find again". Import still re-plans the selection and refuses on a changed fingerprint.
5. **Memory is bounded**: one snapshot per person and connection, a ceiling on the number held, and snapshots are never
   written to storage.

**Not part of this.** Per-group lazy browsing of the review tree, a configurable limit, and scoped (non-all-assets) import.

**Open.** How many objects real systems hold (the 50,000 figure is a guess to be replaced by a measured one).

### 15.x Built (web)

The Find screen now shows the total found, a filter per grouping field (with counts) and a search box. Both ask the Server to narrow the kept list (`/discover/search`) rather than filtering the first 1,000 in the browser. Review and Import pass the same snapshot, words and chosen values, so only the chosen part is planned and imported. Changing the choice discards a review that no longer matches. An expired list asks the user to find again.

## 16. PetroVisor sign-in

PetroVisor is deployed in the vendor's cloud and sends people to an identity provider (Keycloak), which decides whether
they sign in with its own accounts or with federated Windows identity. The Gateway runs unattended, so it cannot follow
that browser redirect. Decision:

- The Gateway signs in as a **service account on the same identity provider** (OAuth client-credentials grant). The
  identity provider's own people-facing methods do not matter to it.
- The connection carries only the PetroVisor address, the workspace and an optional **identity provider address** (the
  Keycloak realm address, or its token address). Left empty, PetroVisor's discovery service says where to sign in.
- The client id and secret are set in the Gateway service's environment (`WSA_PETROVISOR_CLIENT_ID`,
  `WSA_PETROVISOR_CLIENT_SECRET`), never in a connection, like the other adapters' credentials.
- The screen states the identity PetroVisor sees ("service account ..."). Per-person identity (a person signing in once
  and the Gateway holding their refresh token) is deliberately not built: it stores a per-person secret and needs a
  "sign in again" flow. It can be added later behind the same seam.
- The testing-only user name and password sign-in stays as a fallback while no client is configured, and is removed
  once the service account is proven in the user's environment.

## 17. Connection roots in the Assets tree (built)

Each Asset has at most one owning Connection (section on ownership above). The Assets tree shows that without a stored
root row: the top level is derived from the owner field.

- `GET /assets/owners` lists the Connections that own Assets the caller may see, then **Standalone**, with counts. A
  group with no visible Asset is not listed, so the list leaks nothing; `ownerConnectionId` or `standalone=true` on
  `GET /assets/children` narrows the top level (both together is a validation error).
- The tree groups top-level Assets under their Connection root in both browse modes (the in-memory tree and the lazy
  server browse). A root is a label, not an Asset: it is named after the Connection (renaming changes only the label),
  carries a count, and links to the Connection. When no Connection owns anything the tree stays flat as before.
- The Asset page says which Connection owns the Asset; search results and children carry the owner.
- Not in this slice: moving Assets between roots, Views, drag and drop (later slices).

## 18. Connection-scoped well Views

A View is a named explicit subset of wells belonging to one Connection. The imported hierarchy locates wells;
group nodes expand and offer an explicit descendant-well selection action. Checkboxes select actual `well`
Assets by ID across branches, search and pages. Selection is static: later imports never join automatically.
Users review selected wells, remove exceptions, and save. The Connection is single-select and immutable after
saving. Local browsing filters remain independent of execution membership.

`assets.asset_views` retains the header and revision; `assets.asset_view_members` stores unique View/Asset
pairs. Names are unique per Connection. The member and search endpoints page records; the selection endpoint
returns the complete ID set plus identifiable unavailable members for authorized maintenance. Partial readers
cannot replace hidden membership. Broad authorized editors can remove inactive members; unavailable members
cannot be assigned. Expected revisions and transactional authorization fences protect writes.

Legacy saved-query rows retain their definition JSON and a null ConnectionId. They are preserved for audit,
excluded from executable View APIs, and never silently converted into a broader well selection.

## 19. Visibility, permissions and logic assignment

Views inherit the owning Connection's read authority. The current Connection model uses deployment-wide
`connections.read`, so its readers see all Connections and their Views. Individual-Connection grants are a
separate authorization enhancement; this implementation does not claim per-Connection grant administration.
Asset restrictions still control member details. There is no personal/shared toggle or copy/publish workflow.

Mutations require `asset-views.create`, `asset-views.edit` or `asset-views.delete` plus Connection access and
member authority. Admin/Engineer defaults include those permissions; custom roles can grant them. Creator and
updater remain audit facts, rather than exclusive edit rights.

Logic setup resolves a selected View and expected revision on the server, preserves declared module
cardinality/type/traits, and stores explicit target bindings plus View ID, revision, Connection and target role.
All roles stay within that Connection. Optional module `requiredAdapterKey` declares adapter compatibility.
Data bindings must use that Connection's adapter and each Asset's stored source identity. Editing a View never
changes an existing configuration's snapshot; reviewing the latest selection creates a new draft revision.
A referenced View cannot be deleted. Disabled Connections block assignment and run admission.

Current scope: assignment to modules whose declared cardinality accepts the complete selected set. A one-well
module requires a one-well View; automatic per-well fan-out from a larger View is not implemented. ForeSite
hierarchy discovery/import is available, but the live engineering read/write provider remains separate adapter
work; the Gateway currently registers simulator current-data routing. Saving an assignment does not implement
that provider or start execution. Existing validation, activation, licensing and execution checks remain in force.
