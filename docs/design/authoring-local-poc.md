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

## Known gaps

AppContainer mode fails in this environment (CreateProcess error 203); job-object mode is the verified baseline;
structured-input calculations are only available as opaque code steps; one asset role; scalar decimal operations.
