# Logic details refinement

Approved scope: organize an existing configured logic into Overview, Configuration and Activity. Keep the surrounding Logic/Modules/Schedules navigation. Preserve the existing module-driven editor, immutable saved revisions, authorization, request idempotency and activation concurrency checks.

## Final design decisions

- A logic module package and a saved configuration revision are separate concepts. This refinement edits neither module code nor an already saved revision. Copying uses the existing new-revision editor and server flow.
- Do not introduce a two-parameter editor, provider-specific wizard, mutable saved drafts, or new limits on parameter/binding counts. Existing structured JSON and provider mappings remain intact. Display all saved parameter and binding metadata with progressive disclosure; do not expose mapping JSON, manifests or credentials.
- Keep the active revision and pending drafts visible in context. Select one revision's settings at a time. Historical revisions stay collapsed until requested. Existing validation and activation review remain authoritative; richer comparisons and complex/multi-provider authoring need a separate design increment.
- Keep the editor mounted in Configuration. Disable tab changes while editing rather than silently discard inputs. A live workspace list update must not remount an editor with the same configuration identity. Access loss still removes protected content.
- Overview shows active targets/providers, module, schedule and two current authorized runs. Activity reuses the execution inspector and keyset history, scoped to configuration before paging. History uses pinned execution scopes, including earlier View memberships.
- A bounded workspace list is not proof that an older linked configuration is inaccessible. The fresh detail read and scoped activity projection remain authoritative; do not close a configuration solely because it ages out of a full list.
- Live operational observations are a separate read projection. They never replace the immutable configuration snapshot being reviewed. If the active revision changed elsewhere, ask for a configuration reload before further mutation.
- Reuse server schedule calculations and existing SignalR invalidation/polling. Keep independent local failure states and clear denied cached data.
- Unified Readiness stays deferred. Do not ship a placeholder tab, readiness score or inferred green verdict. Module approval, connection reachability, input freshness and runtime eligibility are separate evidence.

## Reuse and validation

Reuse ActionButton, Status, LoadingIndicator, setup/Shadow/policy panels, revision settings, run context labels, execution inspector, live-resource hooks, paired-grant SQL and keyset history. Extract only the shared execution-history query from Asset details; do not maintain two paging implementations. No persistence migration or new runtime service.

Verify current revision actions and authorization regressions; complex nested parameters and multiple provider bindings; immutable copy semantics; editor retention on live list refresh; tab keyboard navigation; live runs and reconnect/polling; history paging and scoped parity; independent failures, denied reads and selection races. Run generated-client typecheck, frontend checks, backend build and full PR CI, including PostgreSQL and installer validation, before merging to develop.

## Local verification

Release solution build and generated-client TypeScript/production web build passed. ESLint, 313 node tests and 477 interaction tests passed, including the revised lifecycle/authorization tests and dedicated complex-parameter, multi-provider, live-activity, stale-snapshot and accessibility cases. Chromium checks at 1440, 736, 390 and 320 pixels passed for all three sections and the existing editor, including retention after a changed workspace snapshot. PostgreSQL repository and HTTP regression checks are included in the PR CI gate. No schema or deployment topology changes.
