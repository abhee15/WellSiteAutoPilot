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

The object answers one question: *within what bounds may this logic's commands be carried out?* It is not only about
autonomy: in Recommendation mode it bounds what a person may apply; in Autonomous mode it bounds what the system may do
alone. Candidates:

| Name | For | Against |
| --- | --- | --- |
| **Command limits** | Plain, says what it is, true in both modes, pairs with the site's "Site commands" (what is possible here) | "Limits" might suggest only a maximum |
| Control permissions | Familiar word | Collides with user permissions (RBAC), which also guard control |
| Operating envelope | Used in the field | Jargon; needs explaining |
| Autonomy policy (today) | Existing | Wrong for Recommendation mode; "policy" is vague |

Recommended: **Command limits**, with a mode line under the title: Recommendation - "What a person may apply from this
logic's recommendations"; Autonomous - "What this logic may do on its own". The site's catalog is shown to engineers as
**Site commands**. The code and API are renamed from `policy` to `limits` in the same migration as the mode change (P2b),
while nothing else depends on the old name.

## 3. What a person sees and does (screen S2, replaced)

Shown only for a logic whose live revision is Recommendation or Autonomous (`control-screens.md` 1a). The table is
**generated**, one row per command the module declares for each asset role. There is no Add button.

```text
Command limits                                   Autonomous · revision 3 · version 4 · Off | Dry run | Live
What this logic may do on its own. It can only ask for the commands it declares.
+------------------------------------------------------------------------------------------------+
| Command          Asset role   Available here          Site range     Your limit       Max step   |
| Set pump speed   Well        Yes - 3 of 3 wells       0 to 12 spm    [ 3 ] to [10]    [ 1 ]      |
| Run or stop      Well        2 of 3 wells (Well C:    0 or 1         [ off ]          -          |
|                              not enabled at site)                                               |
| Set choke        Well        No - the adapter cannot  -              not available               |
|                              command (read only)                                                 |
+------------------------------------------------------------------------------------------------+
| How often   At most [ 3 ] commands every [ 10 ] minutes                    (Autonomous only)     |
| Why are you changing this? [____________________]                       [Cancel] [Save as Dry run]|
+------------------------------------------------------------------------------------------------+
```

- **Rows come from the module.** The role and command are read-only. If the module declares nothing, the panel says "This
  logic does not ask for any commands" and offers nothing.
- **"Available here" is computed, not typed**, from the bound assets' connections, the adapter's definitions and the site's
  catalog: Yes (all bound assets), some (names the ones that cannot and why), No (why). A row that is not available for any
  asset cannot be switched on and explains what would fix it and who can ("Ask a connection manager to enable it").
- **Your limit** is empty until the person sets it; it is checked inline against the narrowest site range across the bound
  assets (shown beside it) and the module's declared unit. Nothing is pre-filled silently; a module may later suggest a
  range, shown as a hint the person confirms.
- **Max step** defaults to the site's; the person may only narrow it.
- **State: Off, Dry run, Live.** Saving never goes Live directly for Autonomous: it saves as Dry run (commands are decided and
  recorded but not sent). Live is a second, deliberate action once the Dry run history has been looked at.
- **Recommendation mode** shows the same table without "How often", titled "What a person may apply"; an empty limit means
  Execute is not offered for that command, with the reason on the recommendation.
- **Read-only adapters** (CygNet and ForeSite today): in Recommendation mode the row reads "The adapter cannot send this;
  a person applies it by hand" and is still listed, so the recommendation is meaningful; in Autonomous mode the row is not
  available and the logic cannot go Live (the revision shows why).
- **Every change** needs a reason, is versioned and audited with before and after; a stale save shows one banner and keeps
  the edits; read-only users see the table without inputs.
- **Phone:** each row becomes a card; nothing scrolls sideways.

## 4. How the rows are built (server)

`GET /control/limits/{logicId}` returns, per (asset role, declared command), the resolution for every bound asset:
connection, adapter, whether the adapter defines the command, the site entry (enabled, range, step, settle, deadline), the
effective range (intersection across assets), and a reason code when unavailable. The screen shows what it receives; it
re-derives nothing. The same resolution is what the gate uses at dispatch, so what the screen says is what will happen.

## 5. Where the adapters come in (discovery)

Two kinds of adapter knowledge, both feeding the catalog and never the person typing:

- **Static definitions** (done for the simulator): the adapter says what commands exist and their shape.
- **Discovered targets** (ForeSite, CygNet): for a connection and its assets, the provider lists what can be written (a
  setpoint, a registry value, a command). Discovery proposes catalog entries; an engineer maps a proposal to a platform
  command name, sets the site range, and enables it. This mirrors asset discovery and import, and is how the catalog is
  filled for real providers without typing identifiers.

The owner is providing the ForeSite and CygNet discovery material. What is needed from it, beyond the behaviour sheet in
`control-command-framework.md` section 6:

1. How to list what is writable for a connection, and for one asset (endpoint, paging, permissions needed).
2. What the provider says about each writable item: its id, display name, unit, data type, valid range, and whether the
   provider enforces the range itself.
3. How an item is addressed when written, and how that relates to the asset's id the platform already imports.
4. How a write is acknowledged, and the read that proves the value changed (with latency).
5. Which of the platform's command names each item corresponds to (or whether the mapping must always be made by an engineer).
6. Whether writes need a different credential or role than reads.

## 6. What this changes elsewhere

- `control-screens.md` S2 is replaced by section 3; S1 (Site commands) gains "Discover commands" for adapters that support it.
- The policy becomes `command limits`, keyed by (logic, mode), versioned, state Off / Dry run / Live (`control-rework-plan.md`
  4.2). Rate applies to Autonomous only. A limit exists only for a command the module declares; the server rejects any other.
- The catalog is keyed by (connection, asset type, command) so different asset types on one connection can differ.
- The gate checks, at dispatch, the same four layers the screen showed.

## 7. Decisions for the owner

1. The name: **Command limits** (recommended), or another from section 2.
2. Autonomous cannot save straight to Live; it must be Dry run first. Recommended: yes.
3. A module may suggest a range (an additive manifest field), shown as a hint, never applied silently. Recommended: yes.
4. Where a read-only adapter is bound, Recommendation mode still lists the command as "applied by hand". Recommended: yes.
5. Limits are set per (role, command) across all bound assets, using the narrowest site range; per-asset limits only if a real case needs them. Recommended: yes.
