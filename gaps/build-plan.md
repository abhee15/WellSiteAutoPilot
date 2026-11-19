# Module authoring build: environment, process and tracking

This branch (`gaps/module-authoring`) is never merged. It is the tracking record for the module authoring build.
The code work happens on `feature/module-authoring-build`, branched from `develop` at #681.

## Process

1. Build step by step on `feature/module-authoring-build`, aiming at one complex module that ForeSite can actually run (Idle Time, Target Fillage or Rod Buckling; picked per step by what ForeSite data we can reach).
2. Test locally first, against a fresh database and the live ForeSite server.
3. Then get the GitHub CI pipeline green.
4. When the feature is done, mirror it back into Perforce one CL at a time, as in the regular process.
5. Gaps and Sri's observations are logged here: gaps in `complex-module-poc-findings.md` (P-numbers), observations in `observations.md` (O-numbers).

## Local environment

- Worktree: `D:\WellSiteAutoPilot\module-authoring`, branch `feature/module-authoring-build`.
- Stack: `dev\WellSiteAutoPilot.AppHost` with the gitignored `appsettings.Local.json` (Negotiate auth, own JetStream folder).
- Database: `wsa_modauth`, created fresh. It is separate from the Perforce stack's `wsa_dev`, which is untouched.
- Web: `http://localhost:5080`.
- ForeSite connection `ForeSite usdcfstappts010` (`http://usdcfstappts010:80/`): test result Healthy, acting as the signed-in Windows user.
- The Perforce-side Trunk now matches `develop` through #681 (CLs submitted apart from the last few pending).

## Where the build stands (develop at #681)

| Capability | State |
|---|---|
| Inputs: current values, daily averages | Works in the workbench (live-checked up to #669) |
| Settings, calculations, ordered rules, setpoint recommendations | Added in #670-#675; not live-tested |
| Rules preview and Tests view | Added in #676-#677; not live-tested |
| Submit and review of rules modules | #679-#681; not live-tested |
| History, setpoint and card sources; multiple outputs; richer functions | Missing (P106, P107, P109-P113) |

## Next steps

1. Live-test the existing workbench end to end on the fresh database, and log what breaks.
2. Pick the first complex module and map each of its inputs to something ForeSite can supply.
3. Fix gaps in order of what blocks that module, one small PR at a time.

## Decisions

- The old module authoring (the guided contract editor and its starters) is removed once the new workbench is stable and the feature is done. Until then both exist; the workbench opens only rules drafts (#678).
- Live check on the fresh database: `#authoring/new` creates a module and opens the workbench with Inputs, Settings, Calculations, Rules and Tests parts plus Save and Submit for review. There is no Outputs part (P107, P112).
