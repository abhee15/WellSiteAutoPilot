# WellSite AutoPilot — UX and Workflow Plan

Status: **Proposal for review. No screens are built from this document until it is accepted.**
Date: 2026-10-17. Inputs: the ACL design prototype, a domain/persona analysis, a backend capability
map, an audit of the current web UI, and a deep dive on Assets and Logic.

## 1. Purpose and the one-sentence goal

A person who has never seen the product should be able to open it, understand what they are looking
at, know what they can do next, and do it safely — without reading documentation.

Everything below serves that. It organises the product around **what a person is trying to do**, not
around the database tables the API happens to expose.

## 2. What we found

### 2.1 The backend is smaller than the prototype

The prototype shows a rich product (traits, asset groups, relationships, per-asset instances,
readiness, deployments, module onboarding, operating modes, live activity). The real backend today has:

- Asset types, assets (parent pointer only), logic modules, configured logic with immutable
  revisions, asset and data bindings, an optional schedule, **Shadow** executions, recommendations
  (approve/reject records a decision only), grant-based access, audit rows (no read API).
- No realtime push, no health history, no live values for the browser, no control actions.

The plan therefore separates **what we can build now** from **what the design anticipates** (section 12).
We design the whole, build the part the backend supports, and mark the rest as visibly "not yet"
rather than hiding it or faking it.

### 2.2 The current UI is organised by API, not by task

- Five sections; Access has no navigation entry. Pages are long stacks of cards, tables and forms.
- Asset selection is a dead end (name, type, GUID). No path from asset → logic → runs → recommendations.
- Logic setup is one flat form with raw JSON parameters, hand-typed provider IDs and a UTC timestamp.
- Operations shows an empty Asset column and a module ID instead of the logic name.
- Polling replaces tables with "Loading…" and remounts panels, discarding typed input.
- Nothing is linkable; state lives in components.
- Several words are used for the same thing ("Configured Logic", "configuration", "logic workspace").

### 2.3 Facts that constrain the design

- Everything runs in **Shadow**. Recommendation mode has a materializer but configured logic cannot
  produce recommendations yet. Autonomous exists as an enum only.
- "Validated" means structurally valid, not "ready to work on real data".
- No endpoint can change a module's trust or enablement, so the module catalog is empty in practice.
- The .NET worker only runs a built-in sample module; an authored module cannot run until the worker
  is rebuilt.
- List endpoints cap at 50–500 rows with no paging, totals or filters by asset/logic.

## 3. Who this is for

Personas are inferred (the repository defines none); confirm them in section 13.

| Persona | Job | Needs from the app |
| --- | --- | --- |
| **Field operator / Operations lead** | Know what is running, what is wrong, what needs a decision | An attention-first home, plain status words, one-click path to the cause |
| **Production / reliability engineer** | Put logic onto assets, tune it, prove it, promote it | A guided setup, clear readiness, run history per logic and asset |
| **Approver** | Decide on recommendations | Full context on one screen: what, why, how fresh, how long it is valid |
| **Integration / data owner** | Make data sources feed the logic correctly | Binding health, mapping help, provider status |
| **Module author** (AI-assisted, not a programmer) | Create new logic for a business problem | Describe → generate → test → shadow → promote, without code or CLI |
| **Administrator** | Grant access, govern trust | Waiting-for-access queue, who-can-do-what, module approval |

Landing page defaults follow the permissions a person holds (section 5.3), not a role picker.

## 4. Design principles

1. **Say what it is, say what you can do.** Every screen opens with its purpose in one line and shows
   the next sensible action. Empty states teach the steps.
2. **One name per concept** (section 7). The UI never shows a GUID where a name exists.
3. **Operate, Understand, Diagnose.** Group by task. Plain information first, technical depth one
   disclosure away. Never remove depth; layer it.
4. **Show state with words, shape and colour.** Never colour alone.
5. **Be honest about data.** Value, unit, age, quality and source travel together. Anything not
   current is labelled "last known".
6. **Recorded is not executed.** Approving a recommendation records a decision; nothing is sent to a
   controller. The UI must say so wherever a decision is made.
7. **Mode is always visible.** Shadow / Recommendation / Autonomous appears wherever logic is shown,
   by label and icon, not only colour.
8. **Absent features look absent.** Show what the user cannot do once, with who can; hide the buttons.
   Disabled-but-visible only where the user would otherwise wonder where the control went.
9. **Never lose typed input.** Live updates must not remount forms or reset selection.
10. **Everything is linkable.** Selection, tab and filters live in the URL hash.
11. **Preview before commit.** Consequential actions show what will happen, who it affects and how to undo.
12. **Calm by default.** Routine updates are quiet; only things needing a person raise attention.

## 5. Information architecture

### 5.1 Navigation (left, grouped by intent)

```
OPERATE        Overview (attention-first home)
               Runs                     — what is happening and happened
               Recommendations          — decisions waiting (badge)
UNDERSTAND     Assets                   — what exists and where
               Logic                    — what runs on them
DIAGNOSE &     Data sources             — bindings and provider health   (Phase 3)
CONFIGURE      Activity & audit         — who did what                   (Phase 3)
               Access                   — people and roles               (admin)
Account (inline, bottom): Preferences · Access · Sign out
```

- Order within Operate/Understand follows the prototype's principle: **Assets = what exists,
  Logic = what runs, Runs = what happens.**
- "Overview" replaces the Operations card wall. It is a short list: *needs attention*, *running now*,
  *recent results*, *system status*. No KPI wall.
- Navigation items appear only if the user holds a permission for them. Access gets a real nav entry
  for administrators.
- A user with no permissions never sees an empty app: they see the **Waiting for access** page with
  their sign-in name and a copy button (exists today).
- If the default section is not permitted, redirect to the first permitted one.

### 5.2 Page anatomy (every page)

1. Title + one-line purpose.
2. Primary action (one).
3. Filter/search bar (shared component).
4. List or table with a visible "n of m" line and a loading state that does not replace content.
5. Right-hand **inspector** for the selected row; selection is in the URL.
6. "Related" links: asset → logic → runs → recommendations, and back.

### 5.3 Landing by permission

| Holds | Lands on |
| --- | --- |
| `recommendations.decide` and nothing operational | Recommendations |
| `executions.read` | Overview |
| `assets.read` only | Assets |
| `configured-logic.manage` | Logic |
| `security.manage` only | Access |

### 5.4 Top bar and status bar

Top bar: breadcrumb (area → asset path → logic), one **attention** button (opens the attention list),
one **system** indicator. Status bar: live-connection state, time zone, user. There is one attention
count, defined once (section 9), never three.

## 6. Core workflows

Each workflow lists *entry points*, *steps*, *what the user sees at each decision*, and *what is
deliberately not offered yet*.

### 6.1 Understand an asset (Assets)

**Entry:** Assets nav; breadcrumb link from a run; "Related" link from a recommendation.

Assets page = **tree on the left** (built client-side from `parentAssetId`), **asset page on the right**:

- Header: name, type, path ("Located under …"), status.
- **Overview:** plain-language attributes (labels from the type's schema, not JSON), children count.
- **Logic here:** every configured logic whose revisions bind this asset, with status, mode, last run.
  A button "Put logic on this asset" starts the setup flow with the asset preselected.
- **Runs & recommendations:** recent runs and open recommendations for this asset.
- **Data:** which data requirements are bound for this asset and (Phase 3) their health.

Creating an asset: a **form generated from the asset type's schema** (typed fields, units, required
markers, an example) instead of a JSON text box. The type chooser explains each type in one line.
Create Asset is a button that opens a panel, not an always-open form pushing the list down.

The page has two tabs, **Assets** and **Asset types** (`#assets` and `#assets/types`). Assets is a two-pane
view: browse and search on the left (the hierarchy when nothing is filtered, a flat list with each asset's
location when searching), the selected asset on the right; on narrow screens the two take turns. The add
button reads "Add asset under <selected>" and pre-fills the parent. Asset types lists each type with how many
assets hold it and a link to those assets. Still to come, in order: server-side top-level, paged children and
search so very large hierarchies load lazily; asset page tabs; type details with a readable schema.

Asset types can be defined from the Asset types tab (key, display name and a JSON attribute schema, for people with `assets.manage`); this is deliberately the simple version, to be refined as gaps show (a field builder is the likely next step).

Not offered yet (no API): edit, move, deactivate, delete an asset; edit an asset type. These are shown
in "Coming later" help text on the asset page only if an administrator would look for them; otherwise
absent.

### 6.2 Browse and understand logic (Logic)

Logic page has two lists, clearly separated and explained:

- **Your logic** (configured logic): what is set up for your assets.
- **Available modules**: the library of capabilities that can be configured.

Each module has a **module page** that answers, in words, "what does this do and what does it need?":

> *Pump fillage monitor — reads pump fillage (%) for 1 well. Needs data no older than 60 s, Good
> quality. Runs on a schedule. Publisher X · Approved by your company.*

built from the manifest's roles, data requirements, profile and trust. Followed by "Configure logic"
and "Used by n logic configurations".

A **logic configuration page** is the home for one thing the customer owns:

- Header: name, module and version, **mode**, live revision ("Revision 3 · Live"), schedule summary.
- **Overview:** one sentence of status ("Running every 5 min on 3 assets. Last run 2 min ago —
  completed."), readiness line, next run.
- **Assets** (roles → assets) and **Data** (requirement → source, health in Phase 3).
- **Revisions:** one list, one diff-style summary per revision; a single "New draft from this
  revision" action explains why copying exists ("Revisions never change once saved.").
- **Runs:** history for this logic only (needs server filter; Phase 2).
- **Settings:** schema-driven form (Phase 2/3); JSON editor kept under "Advanced".

### 6.3 Put logic on assets (Configure — guided steps)

Replace the single long form with a stepper (borrowing the prototype's wizard):

1. **Choose module** (shows what it does/needs).
2. **Choose assets** — per role; eligible assets only, with "why not" for ineligible ones; searchable;
   live verdict Compatible / Needs review / Not compatible.
3. **Settings** — form from the parameter schema when available; JSON fallback with an example.
4. **Data** — per requirement, show quantity/unit/max age/quality needed, pick the source, show a
   mapping example. Exceptions listed first. (Auto-suggestion in Phase 3.)
5. **Schedule** — cadence in human units, date-time picker with time zone, preview of next three run
   times.
6. **Review** — "What happens when you continue": assets covered, mode (**Shadow: calculates and
   records, sends nothing**), first run time, what it replaces, how to stop. Then Save draft.

Then **Validate** (button explains what is and is not checked) and **Make live**
(confirmation shows assets, schedule start, and the revision it supersedes).

Rename in the UI: Validated → "Checked", with the checked/unchecked lists visible; Active → "Live";
Superseded → "Replaced". (See section 7 and open question 2.)

### 6.4 Test before trusting (Shadow)

- "Run once now" on a live Shadow revision, with the idempotency key hidden behind plain wording
  ("If this fails to confirm, check **Recent runs** before trying again.").
- The run result opens **in context**: inputs used (value, unit, age, quality), what the logic
  concluded in words, and "what it would have recommended" where applicable.
- After a manual run the user lands on the run, with a link back and a link to the resulting
  recommendation if there is one.
- Failure codes are translated: sentence + the affected requirement + a link to fix it (section 8.4).

### 6.5 Watch it work (Runs, Overview)

- **Overview** lists: *Needs attention* (failed or timed-out runs, undecided recommendations,
  access problems), *Running now*, *Recent results*.
- **Runs** filters by status, logic, asset, time; columns show logic name, asset names, mode, outcome
  in words, duration. Selecting a run opens the inspector; the URL carries the run id.

### 6.6 Decide (Recommendations)

Decision screen shows on **one screen**: what is proposed (current → proposed value, unit), why
(reason in words), which asset (name and path), which logic and revision, input freshness, time left,
and the sentence **"Approving records your decision. It does not send a command."**
Approve and Reject require a reason (Reject mandatory). Separation of duties is surfaced if the
server enforces it.

### 6.7 Create new logic with AI help (Module authoring)

Developers are not writing code; modules will be produced with AI assistance. The product needs a
guided path a non-programmer can follow. This is the largest new capability; it is staged in
section 12 and requires backend work (section 11).

**Stages (one guided flow, resumable):**

1. **Describe** — plain-language goal, which assets and data are involved, example situations.
   The assistant proposes a **contract in words and a form**: asset roles, data needed (quantity,
   unit, max age, quality), settings with defaults and ranges, and outputs. The user edits the form,
   not JSON.
2. **Generate** — behaviour summary and rules in plain language; code and manifest available in a
   "For reviewers" disclosure. Automatic checks run: compiles, no forbidden access, bounded runtime,
   control only through intents.
3. **Dry-run** — run the draft against **simulated or recorded scenarios** (normal, stale data, bad
   quality, missing input). Show inputs and outputs side by side; edit scenarios, rerun. Nothing is
   written against live assets.
4. **Configure** — same wizard as 6.3, with a draft badge: **"Unverified — not approved by your
   company."**
5. **Shadow** — runs on real data with results compared to expectations; "why didn't it run?" explained.
6. **Promote** — request approval with evidence (clean runs, deviations). An approver (different
   person) sets the module's trust. Recommendation mode, then Autonomous, are separate, stricter gates
   that do not exist in the product yet.

Trust labels used everywhere: *Approved by your company · Trusted publisher · Unverified · Signature invalid*.

### 6.8 Administer access

Keep today's three-part administration (waiting queue, roles, grants), but: give it a nav entry,
put the tab in the URL, make tabs keyboard-navigable, search the user picker, and use the
"Waiting for access" wording consistently. Module approval joins this area in Phase 4.

## 7. Concept model and canonical vocabulary

One name per thing. The UI uses these words; API field names do not leak.

| Use | Instead of | One-line definition shown in the UI |
| --- | --- | --- |
| **Asset** | | Something you operate or group things under, like a well or a facility. |
| **Asset type** | | A template that says what information an asset of this kind holds. |
| **Located under** | parent asset | Where the asset sits in your hierarchy. |
| **Logic module** | | A ready-made capability, such as "monitor pump fillage". |
| **Logic** / **logic configuration** | Configured Logic, logic instance, deployment, configuration | A module set up for your assets, with its own settings and schedule. |
| **Revision 3** | revision GUID | A saved version of a logic configuration. Saved revisions never change. |
| **Draft · Checked · Live · Replaced** | Draft · Validated · Active · Superseded | Where a revision is in its life. |
| **Asset role** | role | The part an asset plays in this logic (always "asset role", never bare "role"). |
| **Data source** | provider (technical drill-down only) | Where a needed value comes from. |
| **Settings** / **per-asset settings** | parameters / overrides | Values that tune how the logic behaves. |
| **Run** / **Shadow run** | execution (diagnostics only) | One time the logic calculated. |
| **Run schedule** | cadence | How often it runs automatically. |
| **Shadow** | | Calculates and records results; **sends nothing**. |
| **Recommendation** | | A suggested change waiting for a person's decision. |
| **Access role** | role (security) | A named set of permissions. Distinct from "asset role". |

The plan adds a **glossary popover** (the dotted-underline term → definition) and a Help page
generated from the same table so the words never drift.

Relationship shown on the Logic page and in help:

```
Asset type → Asset (located under another asset)
Logic module → Logic (your setup) → Revision 1…N → Runs → Recommendations
                          ↘ asset roles → Assets    ↘ data sources → provider
```

## 8. Teaching the user while they work

### 8.1 Empty states that guide
- No assets: "Assets describe what you operate. Create your first one." + button.
- No logic: three-step strip (Pick a module → Choose assets → Check & make live).
- No runs: "Logic hasn't run yet. Run it once to see a result." + button when allowed.
- No modules: explain it needs an administrator to approve one; link to Access when the user can.

### 8.2 Readiness ("Is this ready?")
One checklist component, reused everywhere: assets exist and are active, module enabled and trusted,
bindings complete, settings valid, schedule valid, **your access**. Collapsed to one line
("Ready · 2 things to fix") and expandable. Phase 3 adds data freshness/quality checks.

### 8.3 Example values and defaults
Settings forms, attribute forms, schedule picker and mapping fields always show an example or default.

### 8.4 Plain-language errors
Every stable error/failure code gets: a sentence, the field or requirement it refers to, and a "fix
it" link. Codes remain visible in a "Details" disclosure for support.

### 8.5 Inline definitions
The vocabulary table above is the source. Terms first appear with a one-line definition; later with a
hover/focus popover.

## 9. Live updates and attention

- One **attention** definition: failed/timed-out runs, recommendations awaiting a decision, access
  or data problems that block a run. Every count in the app derives from it.
- Updates are quiet: rows update in place; existing content is never replaced by a loading message;
  forms and selection survive refreshes. A subtle "Updated 10:42 UTC" line is announced to
  assistive technology.
- Phase 1: polling per visible section only (not seven global fetches), keep last good data on
  failure with a "may be out of date" banner.
- Phase 2+: server-sent events for run and recommendation changes (backend item B1).

## 10. Safety and trust rules (UI contract)

1. Mode is shown on every logic and run; Autonomous is never the default or preselected.
2. Recorded ≠ executed: decision screens and the recommendation status `Executed` are worded apart.
3. A value always shows unit, age and quality; stale/bad values are labelled and de-emphasised, and
   never used to enable an action.
4. When the connection is lost, values are "last known" and **write-capable actions are disabled or
   re-confirmed**.
5. Writes that might have succeeded show an "uncertain" state and a check-first instruction; they do
   not offer a blind retry.
6. Consequential dialogs state what changes, who is affected and how to reverse it, and capture a
   reason.
7. Dangerous or irreversible actions are never one click.
8. Colour is never the only signal.

## 11. Backend additions the design depends on

Ordered by how much UX value each unlocks. Sizes: S/M/L.

| ID | Addition | Size | Unlocks |
| --- | --- | --- | --- |
| B1 | Run/recommendation filters (asset, logic, revision, module, time), keyset paging, `hasMore`; run list including asset and logic names | M | Per-logic and per-asset history, honest tables |
| B2 | Persist asset and quantity on configured runs; return logic name | S | Fixes empty Asset column |
| B3 | Logic summary fields: last run, next run, last outcome, live revision number | S | Cards without client joins |
| B4 | Asset children and path endpoints; assets-by-logic; `GET /assets?search` | S–M | Asset tree and "Logic here" |
| B5 | Module summary projection (roles, requirements, profile, description, outputs) and manifest `description`/`parameterSchema` | S–M | Module page, settings form |
| B6 | Module trust/enable endpoints with audit and separation of duties | S–M | A usable catalog; promotion |
| B7 | Recommendation lifecycle: expiry, Pending/Expired/Superseded sweep | M | Truthful inbox, countdowns |
| B8 | Read-only audit/activity API with scope filtering | M | Activity page, "who changed this" |
| B9 | Persist `RequiresAttention`; derived attention list | M | One attention model |
| B10 | SignalR change signal for runs, recommendations, configurations, assets and modules (architecture baseline names SignalR; see [roadmap](../development/roadmap.md) D2) | M | No polling, live badges |
| B11 | Provider health proxy and last-success; binding readiness endpoint | M | Data sources page, true readiness |
| B12 | Persist skipped scheduled occurrences | S | "Why didn't it run?" |
| B13 | Asset lifecycle verbs (update, move, deactivate) and asset-type edit; impact query | M | Asset management |
| B14 | Capability hints on runs/assets; structured 403 bodies | S | Consistent permission messaging |
| B15 | User preferences (time zone, units, density) | S | Personalisation |
| B16 | Authoring: draft entity, build/lint service, dry-run/replay API, dynamic worker loading, simulator scenarios | L | Section 6.7 |
| B17 | Live current-value route with quality/age | M | Live values |
| B18 | Recommendation mode for configured logic; control-action lifecycle | L | Real approvals and control |

## 12. Build order

Rule: each increment is one PR, small and complete, usable on its own, and consistent with the plan.
Phases are ordered by what the backend supports today.

**Phase 0 — Foundation (done).** Light slate design tokens, button primitives, contrast tests, preview
tooling (PR #139).

**Status (2026-10-18).** Web-only work from Phases 1 and 2 that needs no new backend is done:
grouped permission-aware navigation with hash deep links, an attention-first Overview, run summaries in
words linked run → logic → asset → recommendation, the asset page with a hierarchy tree and a form
generated from the asset type's schema, the Logic page with a plain status sentence per configuration,
recent runs, and a module "About" panel built from the manifest, a numbered setup flow, and a decision
screen that leads with the proposal and says "Approving records your decision. It does not send a
command." The section is called Runs in the UI (address stays `#operations`).

**Update (2026-10-18, later).** Also done since: a collapsible side panel with icons that remembers its
state; hiding controls a person can never use (Create Asset, Manage access); backend B5 (a module's
optional `description` and `parameterSchema`, with saved settings checked against the schema); a typed
settings form for modules that declare a schema; each revision showing its saved settings with the
module's labels; and "Put logic on this asset". Two items on the old backend list turned out not to need
backend work: the asset page's "Logic on this asset" and the run Asset column are built from data the
list endpoints already return (a revision's asset bindings), so B2 and B4 are now optimisations, not
blockers.

**Still open, in order of value:**
- *Needs a decision from the owner:* palette and fonts (open question 11: Google Fonts or licensed fonts
  are fine, we only need the files and licence), dark and high-contrast themes, sticky footer research.
- *Needs backend:* B3 (last and next run per configuration without a client join), B6 (module trust and
  enable endpoints), B1 live updates, B16 for AI-assisted authoring; B2 and B4 only if the client joins
  become slow or wrong at scale (list endpoints are capped and unpaged).
- *Web-only, not yet built:* shared `DataTable`, `FilterBar`, `Inspector` and `EmptyState` components; a
  Logic page for one configuration that replaces the table plus below-table panel (the detail already
  shows status, runs and settings per revision); revision status words ("Checked", "Live", "Replaced")
  once the vocabulary question 2 is settled; the guided stepper beyond today's numbered steps.

**Retrospective.** What worked: small PRs with local checks first; tests that load real pages; building
only from data already returned by the API. What bit us: file names that differ only by case collide on
the Windows CI runner (keep helper and component names distinct); the sandbox has no .NET or TypeScript
checker, so field names must be audited against the generated types before pushing. Next: run the
design pass (palette, fonts, shared components) once the owner supplies the font details.

**Phase 1 — Shell and shared pieces (no backend change).**
1. Split `App.tsx`: shell, per-section data loading, exported pages.
2. Navigation: grouped, permission-aware, Access entry, redirect to first permitted section, URL-hash
   routing for selection/filters/tabs.
3. Shared components: `PageHeader`, `DataTable`, `FilterBar` (saved views), `Inspector`, `EmptyState`,
   `StatusBadge`, `ReadinessLine`, `Term` (glossary). Loading/empty/error treatment unified;
   last-good-data retention; stop remounting on poll.
4. Vocabulary pass: move hard-coded strings to the catalog, humanise enums, one timestamp formatter.
5. Quick wins from the audit (asset fallback, scroll/focus to inspector, 44px targets, skip link).

**Phase 2 — Assets and Logic made understandable (needs B1–B5).**
6. Assets: tree + asset page + schema-generated create form.
7. Logic: module pages and logic configuration page (single revision list, readable status).
8. Configure stepper (6.3), schedule picker, readable Review.
9. Run detail in words; link run → logic → asset → recommendation.
10. Overview: attention-first home; Recommendations decision screen redesign.

**Phase 3 — Trust and diagnosis (B6, B8, B9, B11, B12).**
11. Module catalog with trust approval; Activity & audit page; Data sources page; readiness with real
    checks; "why didn't it run?".

**Phase 4 — Authoring and promotion (B16, B18).**
12. Guided module authoring (6.7), dry-run, shadow evaluation, promotion approval.

**Later:** live values and trends, Recommendation/Autonomous modes, density and tablet/control-room
layouts, dark/high-contrast themes (intentionally absent today).

Every increment must keep: the existing write-path guarantees (idempotency key, `If-Match`, read-back
checks), the token rules (raw colours only in `tokens.css`), WCAG AA contrast tests, and accessibility
basics (landmarks, focus management, keyboard tabs).

## 13. Open product questions

These do not block Phase 1. Defaults are what the plan assumes.

1. **Mental model.** One logic configuration covering N assets, run as a set (backend today), or one
   setup applied per asset with separate state (prototype). *Default: keep the backend model; show
   per-asset status inside the logic page.*
2. **What "Validated" promises.** Structural only today. *Default: rename to "Checked", list what was
   and was not checked; add data-readiness checks later.*
3. **Who owns module trust and enablement**, and whether AI-authored modules may touch live data in
   Shadow before approval. *Default: administrator approves; unverified modules may run Shadow only
   after an explicit acknowledgement.*
4. **Retire, pause and rollback** semantics for logic, schedules, assets and modules.
5. **Asset lifecycle** (edit/move/deactivate; external references so bindings can be suggested).
6. **Traits:** implement or drop `requiredTraits` (server ignores it).
7. **Access gaps** (#65 invisible children; scoped users with partial lists) — how to explain.
8. **Result contract** for "what it would have recommended" and expected-vs-actual.
9. **Persistence of skipped scheduled runs.**
10. **Preferences** (time zone, units, density) and where they are stored.
11. **Fonts.** IBM Plex needs a packaging decision (self-hosted files in the installer; no CDN on site).
12. **Authoring governance:** who may author, who approves, where code and tests are stored, how much
    generated code a non-programmer must be able to see.
13. **Personas.** Confirm section 3.
14. **Languages and units.** English only for now; US-oilfield/SI units are a preference decision.
15. **Sticky status footer.** Passive status always in view (connection, "Updated hh:mm zone", time
    zone, units, user), with Refresh beside the freshness it refreshes, is common in control-room tools
    and is in the prototype. Page actions and decisions stay beside the thing they act on, never in a
    global footer. To test before building: vertical space on tablets, focus not obscured at high zoom
    (WCAG 2.2), landmark and focus order, coexistence with the sign-in banner, and whether live updates
    (B10) make the Refresh button unnecessary. *Default: no footer until the research supports it.*

## 14. How we will know it works

- Each increment is checked in the rendered preview (`tools/design-preview`) at desktop and tablet
  widths before merge.
- A **first-use walkthrough** is the acceptance test for Phases 2 and 4: someone unfamiliar creates an
  asset, configures logic on it, runs Shadow once, finds the result, and explains in their own words
  what the screen means — without help.
- Automated: unit and interaction tests per component, contrast and token guards, keyboard and
  landmark checks.

## Appendix A — Prototype patterns we keep

Grouped navigation and inline account area; contextual right inspector; critical-context strip pinned
above tabs; quiet activity stream with a separate attention block; engineering value with age/quality/
source; readiness checklist; wizard with live compatibility; reason capture on consequential dialogs;
Available-actions pattern (hide what you cannot do, say once who can); badges pairing colour with word
and shape; platform state shown separately from module state.

## Appendix B — Prototype patterns we do not adopt as-is

- Per-asset instances (conflicts with the backend model; see open question 1).
- Lock-and-send-anyway buttons (we hide and summarise instead).
- Three different attention counts, Recommendations duplicated as a tab, hard-coded demo journey,
  disconnected state that leaves control actions enabled, localStorage-only saved views.

## Appendix C — Current-UI problems this plan resolves

Access hidden from navigation; below-the-table inspectors with no focus handling; poll-driven loading
flicker and panel remounts that discard input; raw JSON and GUIDs; blank Asset column; revisions
rendered twice; no deep links; hard-coded strings and mixed timestamp formats; touch targets under
44px; no skip link or document titles; admin tabs without arrow-key handling.
