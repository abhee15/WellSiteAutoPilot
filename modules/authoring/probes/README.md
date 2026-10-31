# Authoring build/package feasibility probe

Related plan: [PR #320](https://github.com/abhee15/WellSiteAutoPilot/pull/320).
This is a disposable probe over the existing trusted .NET starter, not a hosted
authoring implementation or an isolation test.

Run from the repository root with the repository .NET SDK available:

```sh
python modules/authoring/probes/build_package_probe.py --report modules/authoring/artifacts/build-package-probe.json
```

The script copies the reviewed starter into a temporary workspace, points it at
the product SDK, compiles actual binaries, and reuses the existing CLI to validate,
pack, verify and preflight. It compares every archive payload byte and repeated
packing of the same inputs. It exercises checksum rejection, a warmed-cache
no-restore rebuild, and an intentional compiler error. The temporary workspace
is removed. The report records passed checks and explicit verification limits.
No compiled package is published, uploaded to a catalog or approved.

## Additional targeted probes

See [confidence review](CONFIDENCE-REVIEW.md) for the evidence matrix, independent
review findings, resolved defects and remaining implementation gates.

```sh
dotnet build modules/authoring/probes/composition --configuration Release --disable-build-servers -m:1
dotnet modules/authoring/probes/composition/bin/Release/net10.0/CompositionProbe.dll modules/authoring/artifacts/composition-probe.json
node modules/authoring/probes/document_probe.mjs modules/authoring/artifacts/document-probe.json
python modules/authoring/probes/boundary_probe.py --report modules/authoring/artifacts/boundary-probe.json
python modules/authoring/probes/offline_restore_probe.py --report modules/authoring/artifacts/offline-probe.json
```

```sh
python modules/authoring/probes/windows_isolation_probe.py --report modules/authoring/artifacts/windows-isolation-probe.json
```

The Windows-only isolation probe starts a child in a capability-less AppContainer
with a kill-on-close Job object, then records denied reads/writes/loopback and
whether cancelling the job ends the whole process tree. It measures a mechanism;
it does not deploy a sandbox, and `releaseGate` stays blocked. Recorded:
[isolation-windows.json](evidence/isolation-windows.json).

`workspace.html` is a disposable, fixture-only calculation/dependency design
probe. It loads application tokens/styles but is not the product React shell.
With Playwright available and Chromium installed, run:

```sh
node modules/authoring/probes/workspace_probe.cjs
```

Set `WSA_PLAYWRIGHT_MODULE` to the installed Playwright module path if needed,
and `WSA_CHROMIUM_PATH` to an installed Chromium executable. The browser harness
serves only local repository files on loopback, launches desktop/narrow probes,
checks editable dependencies/condition and document transport, and writes reports
and screenshots under the ignored `modules/authoring/artifacts/workspace` folder.
No product dependency or endpoint is added. The Node console harness tests the
candidate document, not existing CLI compatibility. Browser numeric/validation
semantics are reference-only, not a substitute for the actual .NET SDK host.

## First local evidence

Baseline: `630f1ad4f137488d4f1260781abaf663936e2dd8`.
Environment: Linux x64, .NET SDK 10.0.100. See
[recorded report](evidence/build-package-linux.json).

- Build/package probe passed, including 13 reported checks. The copied starter
  compiled in approximately 3.7 seconds with SDK/restore state already available.
  This is a single observation, not a customer performance guarantee.
- Manifest plus compiled payload produced a 35,996-byte archive in this run.
  Hashes/build bytes may differ across independent build environments; deterministic
  packing was verified using identical compiled payload bytes.
- A rebuild with warmed restore state succeeded using `--no-restore`; this does
  not prove a clean offline customer installation can restore/build.
- Initial default-parallel CLI build failed without a useful diagnostic locally;
  serialized builds with build servers disabled succeeded. The probe uses those
  bounded settings. Root cause of the first failure is not established.

## What remains open

1. Representative Windows isolation under the intended account, including secret
   access denial, filesystem/network boundaries, resource limits, complete process
   tree cancellation and cleanup. A normal CI account does not prove this.
2. Offline SDK/dependency provisioning on a clean customer deployment.
3. Authenticated download and catalog handoff of the exact built artifact; current
   checks do not exercise HTTP, catalog registration or production execution.
4. Authenticated authoring preview without creating operational execution state.
5. Existing-shell UI permissions, validation, stale-build invalidation, and build
   pending/cancel/failure behavior.

The existing authoring-kit CI matrix now runs this probe on Windows and Linux and
retains reports. Runner compilation compatibility is distinct from deployment
isolation. Record actual CI outcomes before claiming Windows verification.

For the authoritative decimal document probe (build composition first):

```sh
node modules/authoring/probes/decimal_conformance.cjs modules/authoring/artifacts/decimal-conformance-probe.json
```

With WSA_CHROMIUM_PATH and WSA_PLAYWRIGHT_MODULE set, the same command also
executes the browser transport checks. WSA_DOTNET can select the installed .NET
executable. Browser checks use a minimal disposable editor, not the product UI.
