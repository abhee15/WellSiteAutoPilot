# Local authoring proof of concept

A development-only implementation of module authoring (see `console-web-authoring-plan.md` and
`modules/authoring/probes/AUTHORING-CONTRACT-CANDIDATE.md`). It is off by default.

## Enable

```
Authoring:LocalBuild:Enabled = true        # dev only; every authoring route except /capabilities returns 404 otherwise
Authoring:LocalBuild:StorePath             # draft + build folder (default under the local app data folder)
Authoring:LocalBuild:WorkRoot              # scratch folder for builds
Authoring:LocalBuild:SdkProjectPath        # product SDK project referenced by generated modules
Authoring:LocalBuild:BuildTimeoutSeconds   # default 240
Authoring:LocalBuild:DotnetPath            # optional dotnet executable
```

Builds run on the local machine with the installed .NET SDK in **trusted local mode**: there is no isolation.
The Windows AppContainer probe in `modules/authoring/probes/windows_isolation_probe.py` is optional hardening
that is not wired in.

## What it does

- Drafts are JSON documents (inputs, parameters, calculations, conditions, outputs, opaque code steps) in a
  file-backed store (`<StorePath>/drafts/<id>/draft.json`, builds under `builds/<buildId>/`). There is no database migration.
- Endpoints under `/api/v1/authoring` (permission `logic.manage`): capabilities, list/create/get/update drafts
  (revision compare-and-swap), validate, build (async, one at a time, cancellable), download package,
  add to catalog.
- Validation, test and build are separate states; any edit makes earlier validation and builds stale. A build is
  pinned to the draft revision it was built from.
- Build generates a .NET project against the product SDK, publishes it, and packs a deterministic `.wsamodule`
  with the same writer the CLI uses.
- "Add to module catalog" uses the normal package upload service: the module is registered untrusted and disabled
  and must still be approved through the existing review flow.
- Starters: `threshold-alert`, `card-calibration` (code-backed V2 sample), `observe-input`.
- Web: Logic > Authoring tab (`#logic/authoring`) with outline/dependencies, calculation workspace, properties,
  and validate/build/download/add actions.

- Test: `POST /authoring/drafts/{id}/test` runs the draft's sample fixtures against the built package in a
  separate process (`tools/WellSiteAutoPilot.AuthoringTestHost`, job-object limited) using the production
  `DotNetModulePackageHost` (V1) or structured offline host (V2). The generated module exposes `AuthoringTrace`
  (step and condition values) as the instrumentation contract; results include outputs, intermediates, fired
  actions and mismatches against expected values. Results are pinned to revision and build and go Stale on edit.

- Live preview: `POST /authoring/preview` (`LogicManage`) reads one current value for an imported asset and quantity
  through the Gateway measurement client, after the Shadow read-plan rules (active imported asset, enabled
  connection, one source binding, adapter-supported quantity) and a `configured-logic.read` asset grant for the
  caller. It creates no execution or plan and cannot write. The Authoring page has a Live data preview panel.
  Verified with fakes (gate, subject, trimmed quantity, denial never reaches the provider); not exercised against a
  live provider in this round.

- Collection steps: a `collection` step reduces a structured (schema-declared) input to a number with `count`, `sum`,
  `min`, `max` or `mean` over a dotted value path (arrays are flattened). A draft with any structured input or code
  step generates a V2 module (`ILogicModuleV2`, schema-checked structured inputs); graph outputs stay scalar. Starter
  `card-peak-graph` reproduces the card peak/mean/count analysis with no code and runs through the V2 host in tests.

- Provider choice: the module properties declare an optional required provider (`foresite` or `cygnet`), which the
  generator writes as `requiredAdapterKey`. Validation rejects unknown keys (case-exact) and scalar inputs whose
  quantity the provider cannot read directly (`AdapterReadRules`). Binding to a connection happens afterwards in
  configured logic, as for any module.

## Known gaps

AppContainer mode fails in this environment (CreateProcess error 203); job-object mode is the verified baseline;
one asset role; collection steps are numeric reductions only (no per-item filtering or structured outputs without
a code step).
