# 6. Run detail

## Purpose
Explain a single run end to end so an engineer can answer: what data did it read, how old was it, what did each calculation produce, what did it decide, and did the gate let anything through. Plan: G4, G6, E10.

Today: `ExecutionDetailPanel` shows summary, asset, logic instance, module, pinned package, connection revision, mode, run type, timings (requested, scheduled for, retrieved, measured at), per-input outcome, result, structured inputs/results, raw output and technical details.

## Who
`executions.read`. Gate decision and control details need `recommendations.read`; the policy link needs `control.manage` to open the editor (read-only otherwise). Raw output and technical details stay behind a disclosure and respect existing role rules.

## Entry points
Operations list row; schedule detail "Last run"; recommendation detail "Open run"; asset detail runs; authoring preview "Compare with a real run"; `#operations/{runId}`.

## Layout
Sections in decision order. Single bordered card in the details pane (`split-layout`), sections are headings, not nested scrollers.

```
Run 7f3a…  ● Completed   Shadow   Scheduled 12:00:15 · took 1.2 s          [Copy link] ✕
Fillage low alert – Well 12 · v3 · connection CYG_DEMO rev 5
┌ Outcome ─────────────────────────────────────────────────────────────────┐
│ Result: Alert · fillage margin 8 %    Proposed: Reduce speed 42 → 38 Hz   │
├ Provider reads ──────────────────────────────────────────────────────────┤
│ Input        Provider  Value   Point time   Data age   Allowed  Verdict   │
│ fillage      CygNet    62 %    12:00:11     4 s        15 min   ● Fresh   │
│ casingPress  CygNet    221 psi 12:00:11     4 s        15 min   ● Fresh   │
│ tubingPress  CygNet    104 psi Oct 28       3 d 3 h    15 min   ⚠ Too old │
│ motorAmps    CygNet    —       —            —          —        ✕ Not mapped│
├ Intermediate values ─────────────────────────────────────────────────────┤
│ Step           Formula              Value   Unit   Depends on             │
│ fillageMargin  limit − fillage       8      %      limit, fillage         │
│ lowFillage     margin > 0            true   —      fillageMargin          │
├ Gate decision (control) ─────────────────────────────────────────────────┤
│ ● Allowed by policy rev 4 · bounds 20–55 Hz · 1 of 3 this hour           │
│ Sent to SIMULATOR · readback 38 Hz · ● Verified      [Open recommendation]│
├ Technical ▸ (correlation id, contract, raw output, pinned package hash)  │
└──────────────────────────────────────────────────────────────────────────┘
```

### Section notes
- **Provider reads:** one row per input read; columns: input, provider (and role for multi-provider), value+unit, point time (from the source), retrieved time, **data age at run time** (retrieved − point time), allowed age (module `maximumAgeSeconds`), verdict. Verdicts: Fresh, Too old, No value, Not numeric, Provider busy, Provider unavailable, Not mapped, Bad quality. Each non-fresh verdict links to guidance (for example "Map this quantity" in the point map, see connections-point-map.md). Multi-provider runs group rows by role.
- **Why a run skipped or failed:** if any required read failed, the Outcome says in words "Skipped: tubingPress is 3 d 3 h old, the limit is 15 min" and highlights the row; no value, no invented result.
- **Intermediate values:** every step in outline order with formula, value, unit, dependencies; values not computed (because an input failed) show "Not calculated" and why. Structured/collection values (existing `execution.structured`) expand inline as a small table, capped with "Show all N".
- **Gate decision:** shown only for modes that can propose or control (Recommendation, Autonomous). Shadow runs show "Shadow mode: no recommendation or command is created." Contents: gate result in words, the policy revision and rule that decided, the sink badge (Simulator / Real equipment), command, value, readback and verification status. Blocked decisions state each failed check. Link to the recommendation and the control action.
- **Compare with preview:** optional "Compare with authoring preview" is a later extension, not designed here.

## States
| State | Behaviour |
|---|---|
| Loading | existing `execution.loading` with LoadingIndicator |
| Not found | existing message; link back to list |
| No access | existing access denied; sections you cannot read (gate) are replaced by a one-line "You do not have access to control details." |
| Running | status Running, sections fill in as data arrives; reads and steps appear progressively |
| Failed | failure code and message in Outcome, reads shown as far as they completed |
| Skipped by data | as above (stale / missing) |
| Stale detail | if the run is old and package was retired or connection revision superseded: info note "Connection settings have changed since this run" with link to revision |
| Error | `role="alert"` "Run details could not be loaded." [Retry] |
| Truncated | existing truncation notices (`connectionsTruncated`) |
| Empty reads | "This run read no provider data." |

## Real-time
- `executions`: refetch the open run (status, reads, steps) while Running and when its status changes; list rows update.
- `control`: refetch the gate decision, readback and verification (these arrive after the run completes).
- `recommendations`: refresh the linked recommendation state.
- Completed runs are immutable; the only live parts are the gate/verification section and the status of a running run. Data age is stored at run time, not recomputed against "now", and the UI says "at run time" to avoid confusion.
- Finer-grained refetch (G2): detail endpoint only, not the whole snapshot.

## Copy
Headings: "Outcome", "Provider reads", "Intermediate values", "Gate decision", "Technical details". Column names: "Data age at run time", "Allowed age", "Point time". Verdicts as listed above. Messages: "Skipped: tubingPress is 3 d 3 h old; the limit is 15 min." / "Shadow mode: no recommendation or command is created."

## Accessibility
- Each section is a labelled region with a heading; tables have captions and `scope`.
- Verdict uses `Status` (shape + word); ages are text.
- Long values (ids, hashes) have `CopyButton`s with accessible names; tooltips are not the only place for absolute timestamps (`<time datetime>` plus visible text in the Technical section).
- Progressive updates while Running announce only the status change ("Run completed"), not each row.
- Detail closes with `CloseButton`; focus returns to the originating row; sections collapse with disclosure buttons (`aria-expanded`).

## Open questions
1. Is data age measured against the point's source time, the provider's retrieval time, or both (the table currently shows both)?
2. Do we persist every intermediate value for every run (storage, H6 retention) or only on request or failure?
3. Who may see gate and control details: everyone with `executions.read`, or only `recommendations.read`?
4. Should raw provider payloads be viewable (support) and under which permission?
5. Do multi-provider runs need per-role timing (one provider slower) shown here?
