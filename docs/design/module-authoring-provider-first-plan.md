# Module authoring: provider-first plan

Status: **revision 2, after independent review round 1. Nothing implemented.** · 2026-11-18 · target: `develop`

Companion to the walkthrough doc "Module Authoring: Provider-First Design" (screens only). This file is the plan.

## 1. Decisions already made with the product owner

1. A new module starts by choosing its **primary provider** (adapter): ForeSite or CygNet. It cannot change afterwards. Moving a
   contract to another provider means a new draft that copies the contract.
2. No "works on both ForeSite and CygNet" mode. Two drafts can share a contract by copy.
3. CygNet modules are **current values only** until CygNet gains discovery and history. The UI says so up front.
4. WAMI is a **later source role**, not a primary provider. Its model file belongs to the ForeSite well. It may be reached through
   ForeSite or through a direct WAMI adapter (being built); decided when the adapter lands.
5. "Copy contract to a new draft" copies the contract only. Sample cases are a separate feature.
6. **No legacy data to protect.** Drafts can be discarded, APIs can break, the schema can be reset.
7. The module list screen may be redesigned (provider filter, provider badge).
8. Process: plan, one or two stronger-model reviews, probes, then implementation in small PRs.

Vocabulary (to avoid three meanings of "provider"): **provider** = the adapter key a module is written for (`foresite`, `cygnet`);
**Connection** = a configured server of a provider (the existing entity); **data origin** = where the provider got a value (ForeSite can front
CygNet data). The future multi-provider idea is called a **source role**, not a connection.

## 2. What exists today (verified in code on `develop`, corrected after review)

| Area | Today |
| --- | --- |
| Manifest | `LogicModuleManifest.RequiredAdapterKey` (optional). The validator only checks it is an identifier; **nothing makes the casing canonical**. Comparisons differ: exact case in `DirectSources.cs:31,33`, `AdapterScheduleFloor.cs:26`, `GuidedLivePreviewService.cs:167,212,268`; ignore-case in `ConfiguredLogicService.cs:50`. |
| What setting the key changes | The module moves onto the **direct-source rules** (`ConfiguredLogicService.cs:498-502`, `ConfiguredLogicRepository.DirectSources.cs:19-34`, `GrantLifecycle.cs:124-125` dependency check): assignment needs a subject, **exactly one target asset (a leaf, no active children), exactly one source binding with a matching resource kind, a binding provider equal to the connection's adapter, and an adapter that has a read rule**. Modules with no key skip all of this. Workers never read the key; it is checked when an assignment is saved, validated, activated or run manually. The web assignment panel already filters by it and hides assets with no connection. |
| Draft storage | `logic.guided_module_drafts(Id, OwnerUserId, Revision, ContentJson jsonb)`; submissions in `logic.guided_module_submissions` with an FK to drafts (`Restrict`). The repository also refuses to delete any draft that has a submission (`GuidedModuleDraftRepository.cs:77-79`). Pending submissions are approved from package bytes without loading the draft (`GuidedModuleDraftService.cs:54-68`). |
| Draft content | `GuidedModuleDraftContent(Manifest, Definition?, SampleCases?, SourceHints?)`. Identity, version and entry point are immutable on edit. |
| New draft | `blankContractManifest` omits the key, so a contract-only draft has no provider. The package builder serialises the whole manifest, so a key set on the draft flows into the package. |
| Pickers | `GuidedContractEditor` offers every registered scalar quantity and every structured read, from every adapter, grouped by adapter display name. Discovery (Surveillance, DailyAverages, WellTrend, WellTest) is in the live-preview panel; **nothing in authoring offers setpoints, and the runtime has no setpoint read yet**. |
| Compatibility | Per sample asset, per input. `GuidedInputSupport.UnsupportedReason` returns `AdapterMismatch`, but an access check runs first, so a non-current input on a wrong-provider asset reports `AccessUnsupported`. A contract-only draft with zero inputs returns `Inputs: []` for a wrong-provider asset, which reads as compatible. No .NET test covers `GuidedLivePreviewService`. |
| Source hints | Validation never compares the hint's adapter with the manifest; a hint with only an operation key is not checked against any adapter. |
| Starters | Threshold fillage runs on ForeSite and CygNet; the pressure-differential starter (`CasingPressure`, `TubingPressure`) is CygNet-only. |
| Adapter facts | `AdapterReadRules` (static) is what a module can actually read: current quantities, structured reads, `ServesDailyQuantities`. The runtime registry also lists `simulator` and PetroVisor (no read rule) and its CygNet entry advertises history when a simulated history client is wired, so "what an adapter serves" must not be derived from the registry. The registry endpoint needs `connections.read`, which the guided-author role does not have. |
| Lists | Draft list: a dropdown over a page of 25, ordered by Guid. Module catalog: `logicCatalog.ts` filters in the browser; `LogicModuleResponse` already carries `ManifestJson`, and the web already reads the key from it (`configuredLogicSetup.ts:18`). |

**Key finding stands:** the provider needs no new concept and no new draft column for correctness; it is `requiredAdapterKey`. But it is not a
free label: setting it changes how the module can be assigned (section 4.2).

## 3. Current design: pros and cons

Pros: provider-neutral contract; registry/read rules already describe operations; specific compatibility reasons; the key is portable and
already enforced at assignment.

Cons: authoring never sets the provider, so pickers are global; "unsupported" mixes wrong provider with unsupported capability; the server
does not refuse inputs a provider cannot serve; a sample test on one provider says nothing about another; the lists cannot be filtered by
provider; the CI end-to-end guided test runs on a provider-less manual asset, which will not survive a required provider.

## 4. Target design

### 4.1 Provider on the contract

- Create requires a provider. The server resolves it with `AdapterReadRules.Find(key)` (exact match, **canonical casing stored**) and sets
  `manifest.requiredAdapterKey`. Errors: `GUIDED_DRAFT_PROVIDER_REQUIRED`, `GUIDED_DRAFT_PROVIDER_UNKNOWN`.
- Edit cannot change it: `GuidedModuleDraft.Edit` adds `GUIDED_DRAFT_PROVIDER_IMMUTABLE` (exact-case compare) beside the identity checks.
- The authoring provider list is a **new small read under `GuidedDraftManage`**, built from `AdapterReadRules` (provider key, display name,
  the module input kinds it supports today: current quantities, structured reads, daily fields) with labels from the registry when available.
  It does not depend on the Gateway or on `connections.read`. Cards read from this, so "CygNet: current values only" is the read rule, not copy.
- Create and edit validate each input against the primary provider's read rule (the asset-free part of `GuidedInputSupport.UnsupportedReason`):
  daily or cards on CygNet and pressure on ForeSite are refused by the server, not just hidden in the web.
- Source hints: the stored adapter is derived from the primary. A non-null hint adapter that differs is rejected; operation keys are checked
  against the primary provider's operations when hints are used.

### 4.2 What a keyed module means (a product decision to confirm)

Setting the provider puts the module on the direct-source rules. Consequence: **a guided module runs only on imported leaf assets with one
source binding whose connection is of that provider.** It can no longer be assigned to a manually created asset or to a parent well. This
is consistent with the intent (a provider-specific module) but is a real change from today. Confirm before PR 1.

### 4.3 Multi-provider later (design only, not built)

A **source role** is a named provider plus a kind (data, model, calculation). The primary role is implicit and equals `requiredAdapterKey`.
This is **not free**: today every path assumes one asset and one source (`GuidedLivePreviewService.cs:509`, `DirectSources.cs:31-34`, and the
manifest comment that all targets belong to a Connection using this adapter). A second source needs per-input binding providers and a changed
direct-source check. What 4.1 and 4.2 do give: the primary stays a plain provider key, so those changes extend rather than undo it.

### 4.4 Lists and starters

- Draft list: name, provider badge, revision, state. Server-side filter (`adapterKey`) and search (`search`) parameters and a meaningful sort,
  because the list is paged and a browser filter would only see one page. A non-null `AdapterKey` column on drafts, filled from the manifest on
  save, makes the filter cheap.
- Module catalog: read the key from `ManifestJson` in the web (no new field); add a provider filter and badge; show an "unspecified" bucket for
  approved modules with no key (for example the structured-cards sample) if they stay in the catalog.
- New module: step one is the provider chooser, then only the starters valid for that provider (fillage: both; pressure differential: CygNet).
- Compatibility: add an asset-level reason to `GuidedAssetCompatibilityResponse` so a wrong-provider asset is reported once, including for
  drafts with no inputs.

## 5. Schema and API changes

| Change | Kind |
| --- | --- |
| Drafts: non-null `AdapterKey` column (from manifest) | EF migration, together with the wipe in section 6 |
| `POST /guided-module-drafts` | requires/validates the provider; new error codes |
| `PUT /guided-module-drafts/{id}` | rejects provider change; validates inputs and hints against the provider |
| Draft list | `adapterKey`, `search` parameters; response carries `AdapterKey` |
| Provider read for authoring | **one new endpoint** (API inventory in `tools/ci/check_api_authorization.py` changes) |
| `GuidedAssetCompatibilityResponse` | asset-level reason code |
| Web | generated client regenerates in CI (OpenAPI not committed); new error words in `words.ts` |

## 6. Clean break (revised after review)

Reject-at-load (earlier option 6b) is wrong: delete loads the draft first, the list loads whole pages, and submitted drafts can never be
deleted through the API, so old drafts would be stuck and pending legacy submissions could still be approved into provider-less modules.

**Chosen:** one EF migration that deletes all submission rows and then all draft rows, and adds the `AdapterKey` column. No default-provider code.
Approved modules already in the catalog are untouched; they show under "unspecified". The CI database starts empty, so the migration is
exercised on the Postgres job.

## 7. Risks

- **Server enforces, web assists.** Every filter has a server rule behind it.
- **CI end-to-end guided test breaks.** `ConfiguredShadowE2e.cs` creates a provider-less guided draft on a manually created asset with provider
  `simulator`. With a required provider it needs an imported ForeSite simulator well (the simulator emulates ForeSite). The pressure-differential
  case needs a CygNet read the CI does not have; it either gets a simulated CygNet guided path or is dropped from that test. This is PR 0.
- **Fixtures.** Existing tests create drafts and manifests without a provider (about four .NET files and four web files, plus the visual
  baseline `src/web/tests/visual/guided-authoring.tsx`).
- **Schedule floors.** No adapter sets one today, but keyed guided modules start inheriting a provider's floor.
- **Over-building roles.** Section 4.3 stays design only.

## 8. Probes (assumptions to prove, run in CI as throwaway checks)

1. `AdapterReadRules` can answer, per provider, current quantities / structured reads / daily support with no new code. (Registry-based
   derivation is rejected, see section 2.)
2. A key set on a draft survives save, reload, submit, package build and approval unchanged. (Package builder serialises the manifest: confirmed by reading; one check to prove it.)
3. Assignment of a `cygnet`-keyed module to a ForeSite asset fails with a specific reason, and the reverse; assignment to a parent well and to
   a manually created asset fails for a keyed module.
4. `UnsupportedReason` precedence for a wrong-provider asset (access check first) and the zero-input case.
5. The server refuses daily/cards inputs on a CygNet draft and pressure inputs on a ForeSite draft when called directly.
6. A source hint naming another adapter is rejected, and a hint with an operation the provider lacks is rejected.
7. Key casing: `ForeSite` is rejected on create; the stored key is canonical.

## 9. Build sequence (small PRs, each green on its own)

0. Migration (wipe + `AdapterKey` column) and rewrite `ConfiguredShadowE2e` onto an imported ForeSite simulator well; still no provider rule.
1. Server: provider required/canonical/immutable, inputs and hints validated against the read rule, provider read endpoint, compatibility reason,
   error words, fixtures, new service tests. Run probes 3-7 here.
2. Web: provider chooser step one, header, filtered pickers and sections, starters by provider, wrong-provider wording.
3. Draft list and catalog: server filter/search, badges, "unspecified" bucket.
4. ForeSite setpoint inputs, **after** a decision on the runtime read (no setpoint runtime capability exists yet).
5. Copy contract to a new draft: new module id, inputs rechecked against the new provider.
6. Later: source roles and WAMI.

## 10. Open items for the product owner

- Confirm 4.2: guided modules run only on imported leaf assets with one source of the module's provider.
- Do approved modules with no provider stay in the catalog (shown as "unspecified"), or should they be removed in the wipe?
- Setpoints: confirm they are a runtime read to build (PR 4) and not just an authoring list.

## 11. Questions for review round 2

- Does the revised 4.1/4.2 hold together, and is anything in PR 0 or PR 1 still missing?
- Is the `AdapterKey` column worth it versus filtering on `ContentJson`?
- Anything in 4.3 that 4.1 or 4.2 would make harder?
