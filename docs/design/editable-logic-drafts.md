# Revision editor and editable drafts

Approved behavior: a Draft remains editable until validation succeeds. Validated, Active and Superseded revisions are locked; copying creates a new editable draft. Validation failure leaves the Draft unchanged. Saving a draft never activates it or changes the active schedule.

## Implementation

- Reuse the existing configuration editor, module catalog/schema validation, immutable copy flow, authorization fences and lifecycle lock. Group Settings, Targets and overrides, and Data bindings with native disclosure controls. Keep the supported module/version selector and schedule controls.
- Show Edit draft on a selected Shadow draft and Save draft changes in its editor. Copying remains a separate action. Backend draft editing supports the domain's supported execution modes; the browser retains its existing Shadow authoring restriction.
- Reuse AttributeFieldsEditor for scalar and nested object parameter fields. Arrays, conditional/unsupported schemas, nulls, empty structures or unmodeled values retain the complete JSON editor. Only offer return to fields when values round-trip faithfully. Per-asset overrides omit inherited fields and distinguish inherited booleans from explicit false. Provider mappings retain their existing JSON representation and all bindings remain intact.
- PUT `/api/v1/configured-logic/{id}/revisions/{revisionId}` replaces only Draft contents, with `If-Match` carrying the reviewed integer draft version. Each save advances DraftVersion without changing revision identity, number or creation time. Validation also requires the reviewed draft version in If-Match. Version conflicts require reloading; editor contents remain available after a conflict. Uncertain write/read-back outcomes block another submission.
- Under one serializable configuration transaction, check authority over every historical and incoming target, lock the current revision, compare DraftVersion/status, retain View/source dependencies, replace settings and bindings, and audit atomically. A stale validator cannot lock newer settings. Active revision and live schedule state are untouched.
- Retain a draft's pinned View snapshot when its reference and complete target membership are unchanged. Refreshing/changing a View uses the existing reviewed-selection checks. Never turn an old View snapshot into current membership implicitly.

## Persistence and deployment

Generated EF migration `EditableConfiguredLogicDrafts` adds DraftVersion (integer, default 0). Existing drafts start at version 0; locked revisions remain locked. Apply using the normal database migrator before this server version. No new service, runtime topology, provider contract or worker change. Regenerate the TypeScript client from server OpenAPI; do not commit generated client files.

## Validation

Cover same-revision saves, concurrent edits and stale validation, rollback/audit atomicity, old/new Asset scope, failed validation followed by editing, locked revisions, pinned View retention, complex settings/provider round trips, override inheritance, draft selection after saving, accessibility and responsive layout. Full PR CI includes PostgreSQL and Windows installer checks.

The richer activation comparison is a subsequent increment. Unified Readiness remains deferred; validation is not a claim about live connectivity or input quality.
