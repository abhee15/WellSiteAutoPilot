# Control command framework (adapter-neutral, built on the simulator first)

Status: **proposed, for review.** No code accompanies this document.

It turns `provider-command-seam.md` (the safety design for a command that reaches a provider) and section 3 of
`control-operations-followups.md` (what a module may ask for, and how a policy names it) into one build plan that can be
delivered and tested end to end **without any real provider**, using the built-in simulator adapter. Each real adapter
(ForeSite, CygNet, PetroVisor) then plugs in by supplying facts, not by changing the framework.

## 1. Why a framework first

Every adapter will differ in what a command is, how it is addressed, how it is acknowledged and how it is verified. Those
differences are for the adapter owners to state. What must not differ is everything around them: who may enable a command,
how far it may move a value, that it is sent at most once, that "sent" is never reported as "applied", and that every
attempt can be explained afterwards. Building that once, against the simulator, means a real adapter cannot weaken it.

## 2. Corrections to earlier notes (verified against `develop`)

- **A module already declares the commands it needs.** The manifest has a required `commandRequirements` list (`id`,
  `assetRole`, `command`, `quantity`, `canonicalUnit`), validated at registration (unique ids, known asset roles). The earlier
  note that "manifests do not declare commands" was wrong, and the proposal for a new `controlCommands` field is dropped.
  The framework **reuses `commandRequirements`** as the module vocabulary. This needs no manifest version change; an optional
  `suggestedRange` can be added later as an additive field if wanted.
- **Nothing enforces it yet.** The recommendation materializer accepts any control intent command (apart from the reserved
  advisory name), and the policy editor is free text. No shipped sample module emits control intents and all declare an empty
  list, so enforcing the declaration breaks no sample; tests that build intents by hand must declare theirs.
- **The simulator is already a gateway adapter** (`simulator`, current data only), backed by `ProviderSimulator`, which CI
  already runs through the Gateway. It has no command surface. That is where the framework is exercised.

## 3. The model: four vocabularies, each owned by someone

```text
Module   commandRequirements   what the logic may ask for            (module author; exists)
Adapter  command definitions   what this kind of provider can do      (adapter author; new, static)
Site     connection catalog    what is switched on here, and how far  (engineer; new, versioned, audited)
Policy   limits                what this logic may do within that     (operator; exists, becomes bounded by the catalog)
```

A command reaches equipment only if **all four** agree, and each narrows the one before it:
`policy ⊆ catalog (enabled, inside hard limits) ⊆ adapter definitions ∩ module declarations`.

### 3.1 Adapter command definitions (static, no numbers that belong to a site)

`AdapterDescriptor` gains `Commands`, a list the adapter owns. Each entry states the **shape** of a command, never a limit:

- `name` (the platform vocabulary, matched to a module's `command`), `quantity`, `unit`, value kind (number, integer, on/off);
- the **target schema**: a small flat field list (the same `AdapterSettingField` shape used for connection settings) that
  says how a command is addressed at this provider (ForeSite: well data group and element; CygNet: UIS command and registry
  parameter; simulator: a named setpoint). A module can never supply it;
- the **acknowledgement kind** (`Accepted` means only that the provider took it) and the **verification mode**: `ReadBack`
  (a read-only measurement confirms the value, with the quantity to read) or `AckOnly`, which is never treated as verified;
- bounds on what the site may configure for settle time and deadline (so a site cannot set a deadline of zero).

Limits such as minimum and maximum are **not** in the descriptor. They come from engineering at the site, so the framework
has no default that could be mistaken for a safe value: an entry without limits cannot be enabled.

### 3.2 Connection command catalog (the hard ceiling)

Per connection, versioned, edited by people with a dedicated permission (initially `control.manage` plus management of that
connection; two-person approval is phase 2 of `provider-command-seam.md`):

`commandsEnabled` for the connection (default off) and entries of `adapterCommand`, `enabled`, hard `min`/`max`/`maxStep`,
the validated `target` for this site, the verification settings inside the adapter's bounds, and a version. Every change is
one audit record with before and after. A policy limit outside its catalog entry is refused when the policy is saved **and**
again by the gate at dispatch, so a catalog narrowed later also stops an older policy.

### 3.3 Policy and the module

- The policy editor offers the commands that the revision's module declares **and** that the catalog enables for the
  logic's targets. It pre-fills the unit and shows the catalog range as the ceiling. Free text goes away, so the case
  mistake in `control-operations-followups.md` section 3 cannot happen.
- The server rejects a policy for a command the active revision's module does not declare, and a revision change that drops a
  permitted command is flagged on the policy.
- The materializer rejects a control intent naming an undeclared command, with a stated reason, rather than creating a
  recommendation that can never run.

### 3.4 Targets

A command's address comes from the **asset's connection** and the catalog entry, never from the module. A logic whose
module requires an adapter already has exactly one connection per target (`ConfiguredLogicRepository.DirectSources`); the
framework uses that same resolution and refuses with a stated code when a target has no connection, a disabled one, or a
connection whose adapter does not match. The resolved connection, catalog entry and version are stored on the action.

## 4. The path of one command

```text
Gate -> sink (by policy SinkKind) -> [Provider] GatewayControlCommandSink -> Gateway (server caller only)
     -> adapter command provider -> provider                      verification: Gateway read-only path
```

Unchanged from `provider-command-seam.md` section 4: the gate claims one action atomically, re-reads the kill switch,
sends **once**, and never retries a send whose result is unknown. What this framework adds around it:

1. **Provider sink allowed per adapter, not per build.** `SetPolicyAsync` today refuses everything except `Simulator`. It
   becomes: `Provider` is accepted only when every target's adapter is in a deployment list,
   `Control:Providers:AllowedAdapters`, whose default is **`["simulator"]`**. ForeSite and CygNet stay refused without any
   code change until an owner adds them, and the refusal code stays `CONTROL_REAL_WRITES_DISABLED` for them.
2. **Closed answer from the Gateway** (`Sent`, `Rejected`+code, `DryRun`; anything else is ambiguous), mapped onto statuses
   that already exist. The command-route HTTP client is built with retries off and a test fails if that ever changes.
3. **Verification with a settle window and a deadline** (replacing the single immediate readback): a hosted service reads
   back after `settle`, at a bounded interval to the `deadline`, and settles the action. Mismatch is Ambiguous at once. An
   unverified action becomes Ambiguous at the deadline, never silently Dispatched forever. Operator verify and resolve keep
   working at any time.
4. **Stuck `Requested` sweeper:** a claimed action that never reached the sink becomes Ambiguous after a bound, once.
5. **Dry-run:** a per-policy sink kind that reads the current value, records the would-be write and sends nothing.

## 5. The simulator is a real adapter for this purpose

The simulator adapter declares commands the same way a real one will (for example a named speed setpoint and a pump on/off),
and `ProviderSimulator` gains a command surface with the same faults the in-process simulator has: fail before applying, fail
after applying, slow apply, wrong value, unknown readback, and a settle delay. That makes the entire path testable in CI
("Provider simulator through Gateway") with no mock of the framework itself:

| Behaviour | Proven by |
| --- | --- |
| Policy cannot exceed catalog; catalog narrowed later stops dispatch | persistence and gate tests |
| Undeclared command rejected at materialization and at policy save | materializer and policy tests |
| At most one send per action across retries, duplicate executes and a restart | counting sink test; simulator command counter |
| Rejected is Failed and non-retryable; timeout is Ambiguous | fault matrix through the Gateway |
| Slow apply verifies after settle and not before; never-applies becomes Ambiguous at the deadline | simulator settle delay and a fake clock |
| Worker caller cannot reach command routes; every command route has a policy | Gateway route inventory check |
| ForeSite/CygNet still refused with the default list | policy test |

## 6. What each adapter owner provides (the behaviour sheet)

So that real adapters plug in without redesign, each fills in this sheet. Nothing here is guessed in code.

1. Commands supported: name, meaning, quantity, unit, value kind.
2. How a command is addressed (target fields) and how that maps to an asset's external identifier.
3. Authorisation: the identity the platform sends as, the least-privilege role it needs, and any licence flag.
4. Acknowledgement: what "accepted" means, what it does not prove, the exact success value, and the failure codes that mean
   "provider did not act" versus "unknown".
5. Verification: the read-only measurement that shows the value took effect, the tolerance, typical and worst-case latency
   (so settle and deadline can be set), and whether reading it can itself move the equipment.
6. Duplicate behaviour: what happens if the same command is sent twice (so the at-most-once rule can be audited).
7. Concurrency: other writers of the same value (for example a provider-side optimiser) and how to avoid fighting them.
8. Interlock data available: alarms, shut-in, local override, communication and data quality.
9. Rate or volume limits the provider imposes.

## 7. Delivery plan

Small steps, each merged with green CI. None enables a real write.

| Step | Delivers | Depends on |
| --- | --- | --- |
| F1 | This design | none |
| F2 | `AdapterDescriptor.Commands` and the adapter command definition contract; the simulator declares its commands; read-only API for an adapter's commands; descriptor validation tests | F1 |
| F3 | Connection command catalog: domain, migration, service with the ceiling rules, audited API, permission check; no dispatch | F2 |
| F4 | Policy bounded by the catalog and by the module's `commandRequirements` (server); materializer rejects undeclared commands; allowed-adapters list with the simulator-only default | F3 |
| F5 | `ProviderSimulator` command and verify surface with the fault matrix; Gateway command routes (server caller only) and the route-inventory guard | F2 |
| F6 | `GatewayControlCommandSink`, closed outcomes, no-retry and at-most-once tests, target resolution; `Provider` policy for the simulator adapter only | F4, F5 |
| F7 | Settle-and-deadline verification service and the stuck-`Requested` sweeper | F6 |
| F8 | Web: policy editor from declared and catalog commands, catalog screen, Control history tab | F4, F6 |
| F9 | Dry-run sink and distinct status | F6 |
| later | Phase 1 and 2 safety rules (interlocks, change windows, dual control, circuit breaker) | owner decisions |

F2 to F4 and F5 are independent after F2, so they can proceed in parallel pull requests.

## 8. Guard rails added with it

- The authorisation inventory test extended to Gateway routes: a command route reachable by the worker caller, or without a
  policy, fails CI.
- A convention check that only the control gate calls `IControlCommandSink.DispatchAsync`, mirroring the schedule-resolution
  check.
- A test that fails if a resilience or retry handler is attached to the command client.
- Web copy that says "only the simulator can be commanded" is pinned to the allowed-adapters behaviour, so it cannot go stale.

## 9. Decisions for the owner

1. Confirm reusing `commandRequirements` (no new manifest field).
2. Confirm the allowed-adapters default of simulator only, enabled per adapter by configuration.
3. Which permission edits the catalog: `control.manage` plus connection management at first, a dedicated one later?
4. Are the settle and deadline bounds on the descriptor (adapter-owned) enough, with sites choosing within them?
5. The behaviour sheet (section 6) for each adapter, in your own order of priority.
