# CygNet saved-connection adapter

## Scope and POC evidence

Implementation branch: `feature/cygnet-saved-connection-adapter`, based on `develop`.
Reference: `poc/cygnet-reader`, especially `poc/CYGNET-POC-FINDINGS.md` at `6a5f4be`.
The POC demonstrated test, discovery, import and one Shadow execution on a licensed CygNet host.
This implementation does not merge the POC branch, local AppHost pins, machine settings or demo data.
It supersedes the connection/facility skeleton in draft #311; #303 is already in develop.

The initial supported slice is numeric current-point reads through manual, authorized .NET Shadow
executions. No control writes, scheduled live authority, history or structured CygNet values are enabled.

## Implementation plan and decisions

| POC gap | Implementation |
| --- | --- |
| Connection pointed at FAC/PNT instead of owning service | Save domain/site/owning service (usually UIS). Resolve its associated FAC for facility queries; realtime client uses the owning service. |
| Discovery cap reported success | Enumerate the vendor tag list, inspect at most 5,000 facilities, preserve failed-read/truncation status as incomplete. Preserve full facility identity, description and type. |
| Shared Server code hard-coded ForeSite rules | `IAdapterReadPolicy` declares source kind and readable inputs. ForeSite and portable CygNet adapter projects own their policies; one persistence composition list selects them. Native libraries stay outside Server/persistence. |
| Arbitrary CygNet quantities passed Server checks | Validate against the saved connection's explicit quantity map at lifecycle validation, dispatch and acquisition. Reject history/structured requests and nonempty per-binding provider mappings. |
| Gateway-wide point mapping/domain | Store a bounded JSON quantity map on each connection. Domain/site/service identify the source; mapping edits increment connection revision and invalidate pinned read plans without moving imported Assets. |
| Reads could use foreign service/facility identities | Require the imported facility tag to match the saved site's owning service. Existing read plans bind connection, revision, Asset, source binding, configuration and current paired grants. |
| Timed-out native calls accumulated | One shared native execution lane for check/discovery/read. A canceled/timed-out request retains the lane until the synchronous vendor operation really exits. Cache up to 128 clients per client kind by domain/site/service. |
| Vendor text reached people | Fixed error codes/sentences; raw vendor exceptions stay as inner exceptions inside Gateway. |
| Healthy status flags rejected | Initialized, Updated and StringData/DigitalData/OutputData are neutral, matching the POC finding. Unreliable is Bad; unknown/configurable bits remain Uncertain. Live site acceptance is required. |
| Ambiguous acquisition times/old demo data | Reject missing or unspecified-kind vendor timestamps; normalize explicit Local/UTC timestamps to UTC. Preserve acquired time and separately record retrieval time. Keep module freshness checks; do not loosen to one day for demo data. |
| Ordinary build had no CygNet current route | Always register the authenticated route and portable seam; unavailable native support returns `CYGNET_RUNTIME_UNAVAILABLE`, not 404. Connection test explains the required build. |
| Source comparison assumed ForeSite facts | Known blank non-ForeSite facts compare as blank. Partial discovery never establishes missing facilities. |

The vendor API available in the POC returns a facility tag list; no verified server paging API is
available in this checkout. The implementation bounds record reads and reports incomplete results,
not fabricated pages. The tag-list call itself may allocate more than the record limit inside the vendor API.
A permanently hung native call intentionally blocks the lane; restart the Gateway after investigating.
Process isolation and vendor-level termination are separate deployment work, not claims of this timeout.

## Connection settings

Use Connections → CygNet. The Gateway's Windows service account needs its workstation CAL/licence,
domain directory configuration and network access. All connections use that account; Windows user
impersonation and per-connection credentials are not implemented.

Example values below are illustrative, not live connection details:

```json
{
  "domain": "123",
  "site": "SITE1",
  "service": "UIS",
  "pointMappings": "{\"AveragePumpFillage\":{\"udc\":\"RRLPFILAVG\",\"unit\":\"%\"}}"
}
```

Quantity and field names are case-sensitive. Supply compact JSON in the Point mappings field.
At most 128 quantities and 16,384 characters; each mapping has exactly `udc` and `unit`.
Do not map `PumpFillage` to an average point merely to bypass uncertain quality.
Confirm each quantity's physical meaning and configured engineering unit at the site.
Mappings are connection-wide for this slice; different UDC schemes require separate connections.
Per-Asset overrides remain unsupported rather than silently overriding the saved map.

## Build and deploy for live testing

1. Build the portable solution with `CYGNET_LIBRARY_PATH` unset:
   `dotnet build WellSiteAutoPilot.slnx -c Release`.
2. On the Windows x64 CygNet host, set `CYGNET_LIBRARY_PATH` to the existing vendor assembly directory.
   Build or publish **the Gateway project**, not the portable test solution:
   `dotnet publish src/integration/WellSiteAutoPilot.IntegrationGateway -c Release -o artifacts/cygnet-gateway`.
   This opts the Gateway into `net10.0-windows`/x64 and references the installed vendor DLLs.
   `*64.dll` files are copied to output and publish directories. Do not redistribute vendor libraries
   without the appropriate licence. No COM registration or changes to CygNet are introduced.
3. Deploy the matching Server, Gateway and worker build. Retain existing caller keys and broker settings.
   No database migration or service topology change is required.
4. Run the Gateway under the intended licensed service account. Test the connection, discover, import
   selected `cygnet.facility` leaves using your normal well Asset type, and create a View if desired.
   More than 5,000 returned facilities or failed records must show an incomplete warning.

No new installer distribution of CygNet vendor binaries is added. A default installer build continues
to provide the portable unsupported-runtime result; use the opt-in Gateway output for this acceptance.

## Sample package and live acceptance

A read-only average-fillage module is in `modules/samples/cygnet-input-check`.
With `CYGNET_LIBRARY_PATH` unset, from the repository root:

```powershell
dotnet run --project tools/WellSiteAutoPilot.Cli -c Release -- module validate modules/samples/cygnet-input-check/manifest.json
dotnet publish modules/samples/cygnet-input-check -c Release -o artifacts/cygnet-input-check-payload
dotnet run --project tools/WellSiteAutoPilot.Cli -c Release -- module pack modules/samples/cygnet-input-check/manifest.json artifacts/cygnet-input-check-payload artifacts/cygnet-input-check.wsamodule
```

Upload and approve through the existing package catalog. Select an imported facility for the well role,
provider `cygnet`, the exact stored `SITE.SERVICE::FACILITYID` external ID and `{}` providerMappingJson.
Validate, activate and request manual Shadow. The sample requires `AveragePumpFillage`, percent units,
Good quality and an acquisition no older than 60 seconds. Old demo points should fail freshness;
use a point with current acquisition evidence for a successful acceptance run.

Check all of the following on your host:

- Test runs a real facility query; wrong/unreachable owning service fails safely.
- Discovery retains descriptions/type and reports an honest complete/partial status.
- The approved sample completes and records quantity, value, units, quality, acquired time and retrieval time.
  Confirm the installed API supplies an explicit DateTime Kind. An unspecified Kind fails closed;
  establish the vendor/site time contract before changing that behavior.
- Unknown/configurable quality flags remain Uncertain and the sample rejects them. Confirm neutral flag
  interpretation at the site; do not remove an unknown bit to make a test pass.
- Identical facility tags in two connections remain isolated by connection/domain. A disabled connection,
  changed mapping/revision, changed source/configuration or revoked grant prevents subsequent acquisition.
- Unmapped quantity, wrong service prefix, missing point, nonnumeric value and stale input are rejected.
- A slow discovery/read returns a bounded failure. Repeated requests do not accumulate native calls;
  concurrent discovery and execution may queue because the lane is deliberately shared.
- Source checks/reimport preserve accepted baselines; partial discovery cannot report a missing facility.

## Verification boundaries

Portable tests exercise connection validation, mapping, point evidence, discovery completeness, native-lane
cancellation, runtime absence and HTTP server/worker authorization. Existing persistence and package/NATS
fixtures also run CygNet cases with the native point reader substituted. These prove WSA mechanics,
not vendor assembly compatibility, licensing, native deployment or real data semantics. Live CygNet
compilation and acceptance require the owner's host and are explicitly pending.

Local verification for this implementation: full portable Release build passed with zero warnings/errors;
52 CygNet checks, Gateway HTTP caller/runtime checks, ForeSite adapter regressions, source/import and API
contract checks passed. The sample manifest, publish, package and verification passed; all 15 convention
guard tests and the architecture/whitespace checks passed. Database/NATS fixtures are added and compile,
but have not run in this local environment. Hosted CI is pending publication; native CygNet compilation
and live acceptance remain pending on the owner's host.
