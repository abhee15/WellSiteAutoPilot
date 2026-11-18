# Module authoring: provider-first plan

Status: **proposal for review, nothing implemented** · 2026-11-18 · target: `develop`

Companion to the walkthrough doc "Module Authoring: Provider-First Design" (screens only). This file is the plan: what exists,
what changes, what could go wrong, what to probe first.

## 1. Decisions already made with the product owner

1. A new module starts by choosing its **primary provider**: ForeSite or CygNet. It cannot change afterwards. Moving a contract
   to another provider means a new draft that copies the contract.
2. No "works on both ForeSite and CygNet" mode. Two drafts can share a contract by copy.
3. CygNet modules are **current values only** until CygNet gains discovery and history. The UI says so up front.
4. WAMI is a **later connection**, not a primary provider. Its model file belongs to the ForeSite well. It may be reached through
   ForeSite or through a direct WAMI adapter (being built); that is decided when the adapter lands.
5. "Copy contract to a new draft" copies the contract only. Sample cases are a separate feature, decided later.
6. **No legacy data to protect.** Existing drafts can be discarded, APIs can break, and the schema can be reset if that is simpler.
7. The module list screen may be redesigned (provider filter, provider badge).
8. Process: plan, one or two reviews by a stronger model, probes, then implementation in small PRs.

## 2. What exists today (verified in code on `develop`)

| Area | Today |
| --- | --- |
| Manifest | `LogicModuleManifest.RequiredAdapterKey` already exists (optional). `LogicModuleManifestValidator` checks it is an identifier. |
| Where it is enforced | Assignment (`ConfiguredLogicService`), grants (`ConfiguredLogicRepository.GrantLifecycle`), direct sources (`DirectSources`), scheduling floor (`AdapterScheduleFloor`), live preview, history, discovery and compatibility (`GuidedLivePreviewService`, five places). |
| Draft storage | `logic.guided_module_drafts(Id, OwnerUserId, Revision, ContentJson jsonb)`. Provider is not a column. Submissions: `logic.guided_module_submissions` with FK to drafts (`Restrict`). |
| Draft content | `GuidedModuleDraftContent(Manifest, Definition?, SampleCases?, SourceHints?)`. Identity, version and entry point are immutable across edits. |
| New draft | The web `blankContractManifest` omits `requiredAdapterKey`, so a new contract-only draft has no provider. |
| Pickers | `GuidedContractEditor` offers every registered scalar quantity from every adapter, with the adapter names beside it. Daily and setpoint fields are offered only by the live-preview discovery panel. |
| Compatibility | Per sample asset, per input; the asset's connection decides the adapter. `GuidedInputSupport.UnsupportedReason` already takes the manifest's required adapter and returns reasons such as `AdapterMismatch`. |
| Source hints | Per input: adapter, operation, parameters, required flag. Stored beside the manifest. |
| Module list | Logic catalog (`logicCatalog.ts`, `LogicCatalogControls.tsx`) filters by search, eligibility, runtime and sort. `LogicModuleResponse` does not expose the required adapter. Drafts are a plain dropdown in `GuidedAuthoringPage`. |

**Key finding:** the primary provider does not need a new concept or a schema change. It is `requiredAdapterKey`, already portable,
already validated, already enforced at assignment and runtime. The plan is to make authoring set it, lock it, filter by it and show it.

## 3. Current design: pros and cons

Pros: the provider-neutral contract works; the adapter registry already describes operations; compatibility reasons are specific;
`requiredAdapterKey` is enforced where it matters.

Cons: authoring never sets the provider, so every picker is global; "unsupported" mixes wrong provider with unsupported capability;
daily fields and setpoints are provider-specific yet appear in the generic flow; a sample-asset test for one provider says nothing
about another; the module list cannot be filtered by provider.

## 4. Target design

### 4.1 Provider on the contract

- Creating a draft requires a provider. The server sets `manifest.requiredAdapterKey` and rejects creation without a known adapter key
  (`GUIDED_DRAFT_PROVIDER_REQUIRED`, `GUIDED_DRAFT_PROVIDER_UNKNOWN`).
- Edits cannot change it: extend `GuidedModuleDraft.Edit` with `GUIDED_DRAFT_PROVIDER_IMMUTABLE`, next to the identity checks.
- The provider list comes from the adapter registry (`adapters.ListAsync`), not a constant. A new adapter appears as a new card.
  Each card shows what it serves today, derived from its read operations, so "CygNet: current values only" is data, not copy.

### 4.2 Filtered authoring

- Quantity picker: only quantities the primary adapter serves (current), plus its daily fields and setpoint elements discovered on the
  sample well. Sections an adapter does not serve are not shown, with one line saying why.
- Source hints: the adapter field is fixed to the primary provider. Per-input operation choice stays. (The adapter key remains in the
  stored hint so a later connection model can bind inputs to other providers.)
- Compatibility: a sample asset whose connection is another provider is reported as "wrong provider" once, not once per input.

### 4.3 Reusing the same shape for WAMI later

A connection is a named provider plus a kind. The primary connection is implicit and equals `requiredAdapterKey`. Later, extra
connections are listed beside the manifest; hints gain a connection name defaulting to the primary. Nothing in sections 4.1 and 4.2
has to change when that arrives. Not built now.

### 4.4 Module list

- Draft list: replace the dropdown with a list that shows name, provider badge, revision and state. Filter chips: All, one per provider
  from the registry. Search by name.
- Module catalog: expose the required adapter on `LogicModuleResponse` (read from the manifest), add a provider filter and badge.
- New module: the first step is the provider chooser; then the starter (contract-only or threshold sample).

## 5. Schema and API changes

| Change | Kind |
| --- | --- |
| Draft provider | None in the database: it lives in `ContentJson.Manifest.requiredAdapterKey`. |
| `POST /guided-module-drafts` | Rejects a manifest with no or unknown `requiredAdapterKey`. Contract change in behaviour, not shape. |
| `PUT /guided-module-drafts/{id}` | Rejects a provider change. |
| `GuidedModuleDraftResponse` | Add `AdapterKey` for list display (derived from the manifest). |
| `LogicModuleResponse` | Add `RequiredAdapterKey`. Regenerate the web client. Update the frozen API inventory test only if endpoint count changes (it does not). |
| New read: provider catalog | `GET` of the adapters with a plain summary of what each serves, or reuse the existing adapter operations call and derive it in the web. Preferred: reuse. |
| Legacy drafts | See 6. |

## 6. Clean break for existing drafts

Options: (a) a migration that deletes draft and submission rows without a provider; (b) reject them at load with
`GUIDED_DRAFT_PROVIDER_REQUIRED` and let authors delete them; (c) leave the database alone and treat missing provider as ForeSite.

Recommendation: **(b)**. No destructive migration, no default-provider code, and nothing silently changes. Because there are no
customers, an operator can also wipe the two tables by hand. Submissions that already produced approved packages are untouched either way.
Open point for review: whether `Restrict` on the submission FK makes deleting an old draft awkward in the UI.

## 7. Assumptions to prove with probes

1. The registry can answer "what does adapter X serve" (current quantities, daily capability, setpoints, history) without new code.
2. `requiredAdapterKey` set on a draft manifest survives save, reload, submit, package build and approval unchanged.
3. Assignment of a module with `requiredAdapterKey=cygnet` to a ForeSite asset fails with a specific reason, and the reverse.
4. `GuidedInputSupport.UnsupportedReason` already returns `AdapterMismatch` for a wrong-provider sample asset with the right precedence.
5. A CygNet draft cannot be given a daily or setpoint input through the web path once pickers are filtered; and the server still refuses
   it if the API is called directly.
6. Source hints with an adapter different from the primary are rejected by the server, not only hidden in the web.
7. The package builder carries `requiredAdapterKey` into the portable manifest, so an uploaded package keeps its provider.

## 8. Risks

- **Server must enforce, not the web:** every filter above is a convenience; the server rules (create, edit, hints, assignment) are the guard.
- **Two meanings of "provider":** the adapter key (ForeSite) and a configured connection (a specific ForeSite server). The plan uses adapter
  key for the module and connection for the asset; the UI words must keep them apart.
- **Existing tests** create drafts without a provider. Many test fixtures will need a provider.
- **Hint model:** per-input hints with a free adapter field become redundant for single-provider drafts. Decide whether to keep the field
  editable (no) or derive it.
- **Over-building roles:** do not model connections until the first WAMI module; keep section 4.3 as design only.

## 9. Build sequence (small PRs, each green on its own)

1. Server: provider required on create, immutable on edit, hint adapter must match, response exposes it, tests.
2. Web: provider chooser as step one, provider shown in the header, filtered pickers and sections, wrong-provider compatibility wording.
3. Draft list and module catalog: provider badge and filter; expose the required adapter on the module response.
4. ForeSite setpoint inputs under the new filter (the work paused earlier).
5. Copy contract to a new draft (contract only).
6. Later: connections and WAMI roles.

## 10. Questions for the reviewer

- Is `requiredAdapterKey` the right home for the primary provider, or does it overload a field meant for runtime constraints?
- Is option 6(b) safe given the draft to submission foreign key?
- Anything in the enforcement list in section 2 that would treat a draft-time provider differently from a packaged one?
- Is there a scenario where a single module legitimately needs two adapters of equal standing (not a primary plus a model)?
- What breaks in the API inventory, generated client or tests that the sequence in section 9 does not mention?
