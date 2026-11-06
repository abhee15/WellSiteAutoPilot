# Shared fields, notices and confirmations

Tracking: #467. UI-only foundation from control-ui-audit.md D1; independent of the domain proposals in the open #418 design PR. The current priority is this foundation and the first recommendation-flow migration. Populated Overview aesthetics is queued separately.

## Components and behavior

- TextField (including native date/time), NumberField, TextAreaField and SelectField: native controls, visible labels, one control-border/type/spacing owner, required/read-only/disabled states, generated or caller-supplied IDs. Helper or error is connected by aria-describedby, external descriptions retained, invalid state set explicitly. An error replaces helper text; errors identify the field in words, not color alone.
- CheckboxField and RadioGroup: native keyboard behavior; explicit labels and fieldset/legend for grouped options. No automatic choice or acknowledgement. Required markers are visible; native required state remains available to assistive technology.
- Notice: icon + concise text, semantic tone. Runtime errors use alert; runtime progress/success uses status; static context uses note. Optional dismiss is explicitly named. No timer dismisses persistent failures. Busy progress remains local to its workflow.
- FormActions: one wrapping layout for existing ActionButton controls. Consequential action remains a text-labeled button.
- ConfirmPanel: a non-modal in-page group for the existing consequence/acknowledgement/actions. No new confirmation step, dialog, focus trap, or page overlay.

## First migration

Recommendation decision: proposal and context → existing facts → governance notice → native decision options → required audit reason → existing acknowledgement → Submit decision. A reason below the existing two-character minimum gets a connected error after blur. Expiry, scope, conflicts and server validation remain authoritative. Approving still records a decision only.

Explicit execution: proposal and existing facts → consequence notice and acknowledgement → Execute command. Error/refusal stays in context; progress is announced once; all related inputs/actions stay disabled while pending. The stored recommendation remains the command source. No approval, execution, gate, permission or API contract changes.

## Ownership and rollout

Fields own forms.css; notices own notice.css. Pages may position wrappers but may not override these classes' visual properties. Controls expose native attributes/ref but do not accept arbitrary style/class overrides. Existing legacy controls/notices are inventoried by file/count and can only shrink; new raw usages or growth fail CI. Migrated screens must stay at zero raw form controls and notices. Existing specialized search, multi-select and filter owners remain intact.

Continue one reviewed PR at a time through remaining control forms, Logic assignment/revisions/schedules, then Connections. Do not merge the broader unapproved domain model from #418 as part of this task.

## Validation

Use associated-label/help/error tests, native keyboard/disabled/read-only tests, unique IDs with repeated instances, delayed mutation tests and acknowledgement/conflict regression tests. Render the entire recommendation page before and after in populated, read-only, invalid, refusal and busy states at 1440/900/420/320px; verify focus, contrast, 44px touch targets and no horizontal overflow. Source guards complement browser checks.

Primary guidance: https://www.w3.org/WAI/tutorials/forms/ ; https://www.w3.org/WAI/tutorials/forms/notifications/ ; https://www.carbondesignsystem.com/building-blocks/core/components/form/guidelines

## First-slice evidence

The implementation adds connected short-reason validation and routes the recorded-decision outcome through Notice. Local verification: 349 UI tests and 548 interaction tests; typecheck, production build and lint. Browser checks cover before/after decision, invalid reason, pending submission, retained edits after failure, saved outcome and execution refusal at 1440/900/420/320px; read-only visits retain no decision or execution controls. Native field fixtures verify label/value typography against tokens, required/read-only/disabled states, focus, touch targets and computed contrast. The visual-review workflow publishes form fixtures and whole-page before/after screenshots with JSON audits. No API proxy or live provider is used.

The legacy inventory starts at 47 files with remaining raw controls or message roles. It makes existing debt explicit and rejects new usages or count growth by default; shrinking entries must be removed as migration proceeds. Rendered review remains necessary for legacy selectors and semantic layout. #469 (Recommendations organization/scrolling) and #468 (populated typography/data presentation) are queued for the next prioritization checkpoint.
