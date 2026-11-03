# Command limits: what a logic may ask for, how it is picked and shown

Status: proposed, for the owner's decision. "Command limits" is a working name (section 2). Replaces the "autonomy policy"
concept and screen S2 in `control-screens.md`; fits inside `control-rework-plan.md`.

## 1. The problem

Today the autonomy policy is a free-text list: a person types a command name, a unit and a range. That is wrong in three
ways. A person can mistype a command that no module will ever send. A person can allow a command that this site, this
adapter or this asset cannot carry out, and find out when a command is refused. And the same object is used for two
different decisions (what a person may apply, what the system may do alone) and is called "autonomy" even where nothing
autonomous happens.

**People never create commands.** A command exists because three other parties said so, and the person only decides how far
the logic may go inside what they allowed:

```text
Module   declares   what the logic may ask for, per asset role        (module author, in the manifest)
Adapter  defines    what this kind of provider can carry out          (adapter author, static or discovered)
Site     enables    what is switched on here, and its hard range      (engineer, on the connection)
Logic    limits     what this logic may do inside all of the above    (operator, per mode)   <- this document
```

A command reaches equipment only if all four agree. The screen for the last row must therefore be built from the first
three, never from typing.

## 2. The name

Kept after review: **Command limits**, paired with **Site limit** (the catalog ceiling), as engineering limits pair with
operator limits in process control. Rejected: "Operator limits" (clashes with the operator role), "Operating envelope"
(process-safety term, would make tuning sound safety-rated), "Setpoint limits" (excludes run/stop; ForeSite uses the
phrase for its own limits), "Interlock" (safety vocabulary, never use), "Control permissions" (collides with RBAC).

- Title in both modes: **Command limits**. Subtitles: Recommendation - "What a person can send from this logic's approved
  recommendations."; Autonomous - "What this logic can send on its own, and how often."
- The connection tab is **Commands**; its column in the logic view is **Site limit**; the logic's own column is **This
  logic's limit** (the limit belongs to the logic, not to the person).
- The code, API, tables and localization keys (`commandLimits.*`) are renamed in the same migration as the mode change
  (P2b). "Autonomy policy" is removed from the gate messages and the runbook.

## 3. One record shape, two lifecycles

Both modes share the rows (command, allowed, range) and the same gate check. They do not share a lifecycle:

| | Recommendation | Autonomous |
| --- | --- | --- |
| Accountable for each send | The person who approves | The person who made it live |
| States | **Off / On** | **Off / Dry run / Live** |
| Rate limit | No | Yes (per asset and total) |
| Evidence before start | None (the preview shows what would be written) | A dry-run record |
| System may switch it off (circuit breaker) | No | Yes (Live to Dry run, actor `system:circuit-breaker`) |

A dry run in Recommendation mode would let a person approve and see nothing sent, so it does not exist there.

**Versions.** A record has at most one *live* version and at most one *pending* version. An edit that gives the logic more
room (adds a command, widens a range, raises the rate) creates a pending version; an edit that gives it less room may
replace the live version at once, so tightening never stops live control. The gate decides with the live version and also
records what the pending version would have decided. **Dry run** means a pending version exists and no live one: decisions
are evaluated fully, recorded, never sent, and never counted toward the live rate. Versions are immutable and store the
module id and version and the set of bound assets they were approved against; activating a revision whose module version
or bound assets differ drops a Live record to Dry run, with an event saying why. Schedule or frequency changes do not.

## 4. What a person sees and does (screen S2, replaced)

Shown for the revision being looked at when its mode is Recommendation or Autonomous, drafts included (limits must be
preparable before activation; `control-screens.md` 1a). The list is **generated** from the module's declarations; there is
no Add button. Each command is one card (a list of cards, not a wide table, so the phone is the same layout).

```text
Command limits                                   [ Live ]
Sends up to 3 commands every 10 minutes to 3 wells without asking anyone.        [Turn off]
+-----------------------------------------------------------------------------------------+
| Changes not in force yet: dry run since 14:02 - 37 decisions - 3 would have been refused |
|                                                      [Review] [Go live...] [Discard]    |
+-----------------------------------------------------------------------------------------+
| [x] Set pump speed (Well)                                      Can be sent: 3 of 3 wells |
|     Site limit 0 to 12 spm    This logic's limit [ 3 ] to [ 10 ] spm   hint: last 7 d 4-8 |
| [ ] Run or stop (Well)                                         Can be sent: 2 of 3 wells |
|     Well C: not enabled at the site - ask a connection manager.                          |
| [ ] Set choke (Well)                                           Cannot be sent here       |
|     The adapter cannot command (read only).                                              |
| How often  At most [ 3 ] per well and [ 6 ] in total every [ 10 ] minutes                |
|     This logic runs every 5 minutes, so at most 2 per well per 10 minutes is possible.   |
| Why are you changing this? [____]            [Cancel] [Save as dry run]  [History]       |
+-----------------------------------------------------------------------------------------+
```

- **Rows come from the module.** Command and role are read-only. A module that declares nothing: "This logic does not ask
  for any commands."
- **Explicit "Allowed" checkbox**, off by default. An empty range never means "not allowed". Run/stop is a choice ("May
  start", "May stop"), not a range.
- **"Can be sent" is computed** by one shared availability resolver (server) used by the screen, the activation check and
  the gate: all assets, some (names the ones that cannot and why), none (why, and who can fix it). The reason sits on its
  own line under the row only when it applies.
- **Hints, never defaults:** the site limit, the module's suggested range (an additive manifest field) and the recently
  observed range. Nothing is pre-filled. The rate is in minutes or hours and derived from the schedule.
- **State is a badge with a sentence**, changed by buttons (Turn off, Go live...). The revision number appears only when it
  differs from the live one; version numbers appear only in History.
- **Go live** confirmation lists what Live allows, the dry-run summary (decisions, refusals by reason, lowest and highest
  value), and a required reason. Until the evidence threshold is met it reads "Go live is available after N hours or M
  decisions" in place of the button.
- **History:** versions and events (who, why, when) with a diff showing only changed cells ("10 to 11 spm").
- **Cut from the first version:** Max step (not enforced until P3, so showing it would be untrue); Dry run in
  Recommendation mode; long "available here" sentences in cells.
- **Read-only adapters:** Recommendation mode lists the command as "Applied by hand" so recommendations stay meaningful;
  Autonomous shows it as cannot be sent and the revision cannot go Live. States: read-only (no inputs), stale save (one
  banner, edits kept), load error (one alert, Retry), nothing available ("This logic can't send anything here" and who
  can fix it).

## 5. How the rows are built (server)

Four vocabularies sit under one rule, `policy ⊆ site catalog ⊆ adapter ∩ module`, plus a fifth that was missing:

1. **Platform command vocabulary** (new, code-owned, versioned): command name, quantity, canonical unit, value kind
   (number, whole number, on/off, enumeration, action). Module manifests are validated against it, and every adapter maps
   to it. Today names match by string coincidence (`SET_SPEED` exists only in the simulator), which cannot work for
   adapter-neutral control. Added now because adding it later migrates every manifest, catalog entry and limit.
2. **Module requirement** (`commandRequirements`): gains a stable requirement id per (role, command) so a limit row is not
   keyed by a string that a module upgrade can silently reinterpret.
3. **Adapter write mechanisms**, **4. Site catalog**, as in the connection framework.

`GET /control/logic/{id}/command-limits/{mode}` returns the live version, the pending version and, per row and per bound
asset, the resolution (connection, adapter, definition, site entry, effective range as the intersection, reason code).
`PUT` (rows, rate, reason, expected version), `POST go-live {version, reason}`, `POST turn-off`, `GET history`. The screen
shows what it receives and re-derives nothing. Tables: `control.command_limits`, `control.command_limit_versions`
(immutable), `control.command_limit_events` (state changes including system ones).

Code changes this implies: `ModuleCommandDeclarations.Read` currently ignores the asset role, so saving a Well limit fails
when the same logic binds a Separator (live bug, fixed in P2b); there is no scope check on policy writes at all today
(F7); a limit without a unit is never unit-checked (`Evaluate` skips it) and must become required; the rate counter counts
every origin and must count Autonomous-origin actions only.

**Permissions.** Edit: `control.manage` + `configured-logic.manage` scoped to the logic + read on every bound asset.
Go live: `control.manage` with a recorded reason, optionally a person other than the version's author. Turn off: anyone who
can edit, immediately, no confirmation. A dedicated `control.approve` permission is named now even if no role holds it.

**Where future safety rules live (so this record is not redesigned):** hard range, step, tolerance, settle -> catalog
entry; interlocks (alarm, shut-in, local override, stale comms, bad quality) -> a per-asset "safety check" step before
sending, whose results are stored on the action; change windows -> site or deployment layer; drift against a baseline ->
catalog ceiling that a logic may narrow; dual control -> the go-live action; circuit breaker -> system state change in the
events table; kill switch -> outside the record (never changes its state). A "first-N human confirmation" rule is not
added: it duplicates Recommendation mode plus a dry run.

**Audit.** Each action stores the limits version id and the state it was decided in, who made that version live, the catalog
version and, for discovered targets, the accepted target fingerprint.

**Migration (only the simulator exists today).** `AllowApprovedRecommendations` -> a Recommendation record (On if enabled).
An enabled Autonomous revision -> an Autonomous record in **Dry run**, never Live (no evidence exists). Limits without a
unit take the module's declared unit; commands the module does not declare are dropped; commands declared for several
roles become one row per role. Every change is an event with actor `system:migration`.

## 6. Command discovery for real adapters (ForeSite, CygNet)

The catalog today refuses any command that is not in the adapter's static definitions, and the Gateway command routes only
accept those names; ForeSite and CygNet declare none. So discovery needs a model, not just a button:

- A discovered item **never creates a command name**. An engineer binds it to a platform command; the adapter may suggest
  only from an explicit, adapter-shipped table of known items, labelled "Suggested".
- **Catalog binding key: (connection, provider template key, platform command)**, where the template key is the provider's
  stable id (ForeSite `wellTypeId`, CygNet device template), not the platform's renameable asset type. Per-asset presence
  comes from discovery and feeds "Can be sent: n of m". Never one entry per asset. The 64-entry cap is revisited.
- **Discovered item** (per-connection snapshot): ids only for identity (connection, template key, item key), address shape,
  display name, raw and normalised unit, data type (number, whole number, on/off, enumeration with states, action), provider
  min/max/step each flagged enforced or merely stated, paired readback address, write mechanism, whether this account may
  write it, first/last seen, and a **shape fingerprint**. Provider limits are a ceiling the site range must sit inside, never
  a pre-filled range.
- **Re-discovery fails closed:** a missing or changed item suspends its binding until an engineer re-accepts it; a changed
  unit, type, address, or a provider range narrowed below the site range suspends; a changed display name does not; an
  incomplete discovery changes nothing. The gate refuses with `COMMAND_TARGET_CHANGED`. `ControlActionTarget` also records
  the accepted fingerprint.
- **Capability:** append `ProviderCapabilityKind.CommandDiscovery = 7` (never renumber); validator: `CommandDiscovery`
  requires `Command`, and `Command` without static definitions requires `CommandDiscovery`; new `ICommandDiscoveryProvider`
  returning a result with `Complete`, `Warnings`, `ActingAs`.
- **Contract gaps fixed now:** `GatewayCommandRequest.Value` is one non-null decimal; value kinds need Enumeration and
  Action; multi-value commands (CygNet `VSDParms`/`CtrlParms`) are out of v1 and discovery reports them as "not supported"
  rather than mapping one part. Readback becomes per item address, and the readback route accepts that address.
- **Safety:** discovery and reads must never write or move equipment. Writes use their own plain client and never go
  through `ForeSiteSession` (it replays after a 401, which breaks "never retry a command"). Reads are an enforced,
  tested allowlist per adapter (ForeSite reads already use POST, so the method is no guard). CygNet UIS commands, even "get"
  ones such as `DG_F_DEV`, poll field devices and are never run by discovery or readback.
- **Single writer:** an asset reachable through two connections (ForeSite on top of CygNet) may have an enabled binding for
  a platform command in at most one, enforced at catalog save and again by the gate.
- **Identity:** one Gateway account today, so the provider's audit shows the service account, not the person. The command
  contract carries `ActingAs`; whether a separate write identity per connection is needed is an owner decision.

**Commands tab (S1) on a connection**, one tab and one table, no wizard: adapter cannot command (plain sentence, no
buttons); can discover and nothing found yet (**Find commands**, or "Import wells first. Commands are found per well type."
with a link); after Find, a header with source, account, time and complete/incomplete, items grouped by template with unit,
type, provider range and "on 14 of 16 wells", and one action per row, **Use as [select]** listing only compatible platform
commands; unmapped items collapse under "Found, not used (n)". "In use" rows show range, step, verify-after, deadline and
On, with the provider's range as helper text and inline refusal outside it; target fields are hidden for discovering
adapters. **Read now** reads the value back on one chosen well (value, unit, time); there is no write-test button in v1
(first real write is the logic's Dry run, then Live). **Check for changes** reuses the source-changes pattern: changed
fingerprints show "Needs review: unit changed from spm to %", are switched off, and a banner counts them. Cut: name-similarity
auto-mapping, per-asset limits, scheduled discovery, hand-typed targets for discovering adapters, and the connection-level
`commandsEnabled` switch (a third off switch beside the entry switch and the kill switch). No web code calls the catalog
endpoints yet, so nothing needs migrating. Find is read-only (`connections.manage`); mapping and enabling need
`control.manage` and `connections.manage`.

**What to request from the owner** (replaces the earlier six items): 1 versions and topology (does ForeSite sit on CygNet,
are the same wells in both); 2 the real command list, one row per command operators use today (template, provider UI name,
id, unit, type, typical range, enforced?, result point, frequency); 3 exact calls to list writable items with redacted
samples, paging and cost; 4 item metadata (unit, type, range, states); 5 one full write request for one well and how its
ids relate to what is imported; 6 write outcomes (success, each rejection code, timeouts, duplicate behaviour, async
confirmation such as the ForeSite DDS token or `CYWAITPT`); 7 which read proves the change, whether it polls the device,
latency; 8 any read that polls a device or queues something; 9 permission/role names, licence, whether a separate write
account is possible, what the provider audits; 10 examples of multi-value, no-value and enumerated commands; 11 other writers
(optimiser, HMI, local override); 12 how templates and setpoints change and how that shows; 13 rate limits; 14 a test
ForeSite and CygNet with a writable well, or captured traffic.

## 7. What this changes elsewhere

- `control-screens.md` S2 is replaced by section 4; S1 becomes the Commands tab of section 6.
- `control-rework-plan.md` 4.2 follows sections 3 and 5; `provider-command-seam.md` 4.7 (dry run chosen per policy through
  `SinkKind`) conflicts and changes: dry run is a limits state, not a sink.
- The gate checks, at dispatch, the same layers the screen showed, through the shared availability resolver.

## 8. Decisions for the owner

1. Name **Command limits** / **Site limit** (recommended).
2. Different states per mode (Recommendation Off/On; Autonomous Off/Dry run/Live), no dry run for Recommendation. Yes.
3. Live plus pending versions: tightening takes effect at once, more room needs a dry run first. Yes.
4. A module or bound-asset change drops Live to Dry run. Yes.
5. Dry-run evidence threshold before Go live (suggest 24 h or 20 decisions).
6. Rate per asset and in total (recommended), or per logic only.
7. Second person required to go live from day one, or only after the pilot.
8. Recommendation mode: allow-list within the site limit, or per-logic ranges too.
9. Dry run keeps recording while the kill switch is on (recommended: yes, with "would have been stopped").
10. Drop the "first-N human confirmation" rule. Yes.
11. A module may suggest a range (hint only). Yes.
12. Platform command vocabulary: code-owned and versioned; who approves a new name.
13. Catalog bindings keyed by provider template; single writer per asset across connections; suspend on any shape change.
14. Multi-value commands and a write-test button out of v1.
15. One Gateway account, or a separate write identity per connection.
16. Wells must be imported before commands can be found; Find needs `connections.manage` alone.
