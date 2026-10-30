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
