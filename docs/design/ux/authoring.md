# 4. Authoring workspace

## Purpose
Let a logic author go from a template to a tested, versioned module package with confidence: see build state, run tests with intermediate values, preview against live data without creating operational runs, compare versions, and choose which provider(s) the logic depends on. Plan: D2–D4, D5, D6, D7, D9, C2/C3.

Today: `AuthoringWorkspace` (drafts list, starters, outline of elements grouped Module/Inputs/Parameters/Calculations/Conditions/Outputs/Proposed actions with reads/used-by, `AuthoringElementEditor`), a development-only trusted-machine notice, and a disabled state when `Authoring:LocalBuild:Enabled` is off.

## Who
- Open, edit, build, test, preview: `logic.manage`. Read-only browsing of drafts and versions: `logic.read`.
- Live-data preview additionally needs the read authority of the user (connection read via the read-plan authority); without `connections.read` the preview tab explains and offers fixtures only.
- Promoting a package to trusted (D8): separate approval flow (`ModuleApprovalControls`), not in this workspace.

## Entry points
Logic > Authoring tab (`#logic/authoring/{draftId}`); "Edit as new draft" from a module version in the catalog; "New draft" from templates; run detail "Test this input in authoring" (future).

## Layout
Three regions: outline (left), editor (centre), and a tabbed result pane (right/bottom). On narrow screens regions stack with a tab bar. Only one scroll region per pane.

```
Authoring › Pump fillage low alert (draft 7)   ● Build passed 12:04   ● Live
[Save] [Build] [Run tests] [Preview live]                [History] [Package ▾]
┌ Outline ───────────┐ ┌ Editor: Calculation "fillageMargin" ──┐ ┌ Results ──────────────┐
│ Module             │ │ Name      [fillageMargin]              │ │ [Tests][Preview][Build]│
│ Inputs             │ │ Expression                             │ │ Tests  3 passed 1 fail │
│  ▸ fillage   CygNet│ │ [ limit - fillage            ]         │ │ ● low fillage  pass    │
│ Parameters         │ │ Reads: limit, fillage  Used by: alert  │ │ ● normal       pass    │
│  ▸ limit  70 %     │ │ Problems (1)                           │ │ ✕ missing data fail    │
│ Calculations       │ │  ⚠ unit mismatch: % vs fraction        │ │   expected Skipped     │
│  ▸ fillageMargin   │ └────────────────────────────────────────┘ │   got Error NO_VALUE   │
│ Conditions …       │                                            │   Intermediate values  │
│ Proposed actions   │                                            │   fillage   62 %       │
│ [Add input ▾]      │                                            │   limit     70 %       │
└────────────────────┘                                            │   margin    8 %        │
                                                                  └────────────────────────┘
```

### Templates
"New draft" opens a template gallery: starter cards (name, what it checks, provider needed, inputs). Templates declare provider and quantities, so the draft starts with a valid binding. Templates are read-only sources; the draft is a copy ("Created from Pump fillage low alert v3").

### Build state (D6)
Header shows one `Status`: Not built, Building…, Passed, Failed, Out of date (edited since last build). The Build tab shows the compiler output with each diagnostic linking to the element. Builds survive reloads because state lives on the server (DB-backed drafts). Isolation line: "Built in an isolated process" or the existing trusted-machine notice when not isolated.

### Test runner (D2)
- Test cases are fixtures: named input sets (values, quality, age) plus expected result and expected outcome (completed, skipped, error with code).
- Run all or one. Results list pass/fail; selecting a case shows **intermediate values** for every input, parameter, calculation and condition in outline order, with units and which element produced each. Failing assertions highlight expected vs actual.
- "Add fixture from preview" copies the last live-preview inputs into a new fixture (values only, marked "from live data at 12:03").
- Edge fixtures offered one click: "Missing value", "Stale data", "Bad quality", "Provider unavailable".

### Live preview (D3)
- Pick an asset (picker limited to what the user may read) and press Preview. Output shows each provider read with value, unit, quality, measured time and age, then the same intermediate values and the result.
- Permanent banner: "Preview only. This does not create a run, recommendation or command." The preview never writes to anything, including the simulator.
- Age gets a warning if above the module's allowed age, reusing the stale presentation of run detail (see run-detail.md).
- Rate-limited: one preview at a time, 5 s cooldown message "Wait 3 s before previewing again."

### Version history and diff (D7)
- History drawer: list of versions (number, author, time, note, state: Draft, Built, Packaged, Approved/Trusted), selecting two shows a diff of elements (added/changed/removed, expression before/after) rather than raw JSON, with a toggle for the raw manifest.
- Actions: "Restore this version as draft", "Compare with current". Restoring creates a new draft revision; history is never rewritten.

### Provider and role choice (D4, C2, D9)
Provider section in the Module element:
```
Data providers
 Role        Provider        Needs
 default     [ CygNet ▾ ]    PumpFillage, CasingPressure     [Remove]
 [Add provider role]
```
- **Single provider (PoC now):** one `default` role; choose the adapter (requiredAdapterKey). Changing it re-validates every input and shows which quantities the new provider cannot supply ("MotorAmps is not mapped for CygNet. Map it in the connection.").
- **Multi-provider (future, manifest V3 named roles):** each input names a role. A role has a capability ("CurrentData", "ModelCalculation") and a provider choice (CygNet, ForeSite, WAMI). The editor shows a matrix of inputs by role, flags inputs with no role, and previews/tests with per-role fixtures. The role names are what a configuration later binds to concrete connections, and this binding step is explained in a one-line help: "Roles are filled in when you configure this logic for an asset."
- The multi-provider UI is hidden until the server reports manifest V3 support (feature flag from capabilities), so V1/V2 authors never see it.
- Effective minimum schedule frequency (see schedules.md) is shown as information: "Fastest allowed schedule: 15 s (set by CygNet)."

## States
| State | Behaviour |
|---|---|
| Authoring off | existing "Authoring is turned off" notice with the configuration key |
| No access | read-only view if `logic.read`; otherwise hidden |
| Loading | skeleton outline; editor disabled |
| Empty drafts | "No drafts yet. Start from a template." plus gallery |
| Build running | spinner and "Building…"; Run tests disabled; edits allowed but mark result stale |
| Build failed | problems list; Run tests disabled with reason |
| Out of date | results tabs show "Results are from an earlier version" warning |
| Test error | harness failure differs from assertion failure: "The test could not run" with technical text |
| Preview error | provider reason codes in words (no value, non-numeric, busy, unavailable, stale) per G6 |
| Autosave | "Saved 12:04" / "Saving…" / error with Retry; conflict if saved elsewhere |
| Unsaved | warning on leaving |

## Real-time
- `modules` area: catalog and version history refresh when packages are uploaded/approved elsewhere.
- Proposed `authoring` area (G2, D6): build state and test results for the open draft update without polling; until it exists, poll the build endpoint every 2 s while building only.
- `connections`: provider availability and data-source changes refresh the preview picker and warning chips.
- Preview and test results are request/response, not pushed; they are never refreshed by signals.
- Typing never triggers refetch of the draft the user is editing.

## Copy
"Run tests", "Preview live data", "Add fixture", "Build passed", "Build failed", "Out of date", "Preview only. This does not create a run, recommendation or command.", "Restore as new draft", "Add provider role", "Intermediate values", "Expected", "Actual".

## Accessibility
- Outline is a tree (`role="tree"` with `aria-level`) or grouped lists with real buttons; roving focus; shortcuts documented in a help popover.
- Result pane tabs follow the tabs pattern with arrow keys; results announced via `role="status"` ("3 tests passed, 1 failed").
- Expression editor is a labelled textarea; no colour-only syntax or error highlighting (icon + text); compiler diagnostics are a list with links back to the element.
- Diff uses text markers ("Added", "Removed") plus colour.
- Provider matrix is a real table with row and column headers.
- The three-region layout reflows to tabs under 48 em; no nested scrollers.

## Open questions
1. Is the expression editor free text, a structured form, or both (D5 collections and structured results)?
2. Draft ownership: single author or shared with locking/conflict handling?
3. Is live preview allowed for users without `executions`-level data rights on that asset (read authority question)?
4. Where do fixtures live: in the package (shipped with the module) or in the draft only?
5. D9: show multi-provider roles in the first redesign, or only after C3 run-time support lands?
6. What is the upload path from authoring to the trusted catalog and who sees "Package signed" (D8)?
