# Phase 2: CygNet historian validation

Tracking: #484. Builds on merged PR #485. Branch: `feature/adapter-provider-operations-phase2`.

## Current status

The portable VHS value decoder and bounded reverse-series projection are tested with synthetic wire entries.
The native candidate selectively ports the `poc/gap-cygnet-history` API calls. It has **not** been compiled against
licensed historian assemblies or run against VHS in this environment. The host CLI and PowerShell runner also need
licensed Windows host verification. Hosted CI success does not validate their native SDK compatibility.

`CygNetApiClient` deliberately does not implement `ICygNetHistoryClient`. The native candidate can be called only
explicitly by the host probe; the installed Gateway still reports live history unsupported and does not advertise it.
Keep the Phase 2 PR draft until the native compile/read gate is satisfied. The external blocker is missing licensed
assemblies and host access.

## Reconciled POC behavior

- Reuse the existing domain/service call gate and client cache. The history wait budget is ten seconds; a blocked
  native call retains its slot until it returns. Cancellation does not manufacture a completed native call.
- Resolve VHS using the facility service association. Candidate historian ID: `SITE.SERVICE:FACILITY_UDC`, taken
  from the POC; confirm this mapping on the target host. Explicit connection point mappings override defaults.
- Decode exactly eight little-endian bytes for floating-point and signed 64-bit entries. Preserve `Int64` through
  decimal conversion without an intermediate double. Reject NaN/infinity/overflow, malformed UTF8 and unsupported
  value types instead of silently dropping entries.
- VHS timestamps are assumed UTC even when their `DateTime.Kind` is unspecified, matching the POC. Reject local-kind
  or missing timestamps. Confirm the UTC assumption on the licensed host, including a non-UTC Windows time zone.
- Preserve reliability flags: Unreliable → Bad; unfamiliar flags → Uncertain; missing status → Unknown. Quality is
  never inferred from a successful transport response. Units come from the explicit mapping, not the historian value.
- Keep `[start, end)` filtering. Require reverse source order; output ascending source timestamps. On truncation,
  return the newest subset with `SAMPLE_LIMIT_REACHED` and `LATEST_SAMPLES_ONLY`. The collection-only interface must
  continue refusing truncated history.
- Enforce the existing 31-day/1000-sample/1 MiB source/256 KiB reply ceilings, a 64 KiB value ceiling and a 10,000-entry
  scan ceiling. These bound entries exposed by the SDK; the SDK may allocate internal buffers before enumeration,
  so verify its paging/memory behavior on the host before activation. Unknown types, corrupt entries and out-of-order enumeration fail with stable diagnostics.
- No daily averages, alarms, provider writes, asset discovery UI, module authoring or preview endpoint is added.

## Run on the licensed Windows host

Use PowerShell 7, .NET 10 and the intended Gateway Windows identity with read access to an approved test facility.
The host needs its normal CygNet libraries plus `CygNet.API.Historian.dll` and `CygNet.Data.Historian.dll`.
No CygNet binaries are checked in or downloaded by this repository.

1. Check out the Phase 2 PR head. Copy `examples/cygnet-history-probe.json` to a local input file and replace the
   synthetic domain/site/service/facility/UDC and interval with the approved test source. Keep one explicit quantity.
2. Run from the repository root:

   ```powershell
   ./tools/ci/Invoke-CygNetHistoryValidation.ps1 `
     -LibraryPath '<licensed CygNet libraries>' `
     -RequestFile '<local request.json>' `
     -EvidenceDirectory '<local evidence folder>'
   ```

3. The script first compiles the actual native project and CLI, then performs one bounded read. It captures build
   output and a summary with actor, loaded historian versions, sample count, times, qualities, units and diagnostics.
   It restores the caller's `CYGNET_LIBRARY_PATH` afterwards and does not change Gateway configuration or capabilities.
4. Add `-IncludeSamples` only when a local result file is needed to compare readings with CygNet. The default summary
   excludes raw values, source IDs and connection settings. Samples remain local; the runner uploads nothing.
5. Compare an ordinary range, a capped dense range, missing/malformed mapping, denied access and cancellation/timeout
   against the source. Verify exact values/types, first/last UTC instants, quality flags, UDC identity and truncation.
   Repeat across a DST boundary using explicit offsets. An empty success proves only that an empty response parsed.

## Promotion gate

Record the PR commit and native assembly versions with the successful build and source comparison evidence. Fix
SDK or behavior differences on this branch and repeat the host checks. Only then implement `ICygNetHistoryClient`
on the native client, run the Gateway authenticated history tests on that host, verify the existing current/discovery
paths, require current-head CI and merge. Native point identifiers or sample correctness must never be inferred from
passing synthetic fixtures. Until this gate passes, Phase 2 and issue #484 remain incomplete.
