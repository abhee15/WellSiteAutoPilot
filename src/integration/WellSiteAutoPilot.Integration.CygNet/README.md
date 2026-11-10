# WellSiteAutoPilot.Integration.CygNet (live CygNet backend)

Reads current CygNet point values through the managed `CygNet.API.*` assemblies already installed on the Gateway's server.
WSA never installs, registers or ships CygNet. The assemblies are loaded at run time from the existing install, so no special
build is needed. The host must hold a CygNet CAL and be able to reach the domain's directory service.

## What it is, and what it is not

- The live backend of the `cygnet` connection adapter (`src\integration\WellSiteAutoPilot.Integration.CygNet.Adapter`):
  connection test, facility discovery and current reads through authorized read plans. See `docs/implementation/cygnet-adapter.md`.
- Builds everywhere with no CygNet files (CygNet types are bound late). A server without CygNet reports
  `CygNetNotInstalledException` / `CYGNET_NOT_INSTALLED` on each CygNet connection.

## Finding CygNet

The first folder holding `CygNet.API.Facilities.dll` wins: the Gateway setting `CygNet:LibraryPath` (appsettings or the
`CygNet__LibraryPath` environment variable), then `CYGNET_LIBRARY_PATH`, then the folder of the registered COM server
`CxCvsLib.CvsClient`, then `C:\CygNet\Support64` and `C:\CygNet\Support`. Set `CygNet:LibraryPath` only for a non-standard install.

## Configure

Nothing is configured on the Gateway. Each CygNet connection (Adapters page) sets its own `domain`, `siteService`, optional`r
`facilityFilter` and `pointMap`. The Gateway's Windows account needs a CygNet CAL and access to that domain. Only quantities
with a UDC mapping (the built-in defaults or the connection's `pointMap`) can be read; anything else is refused with
`CYGNET_QUANTITY_UNSUPPORTED` and a quantity name is never used as a UDC. The asset external id is a facility tag,
`SITE.SERVICE::FACILITYID` (for example `WSANET.UIS::WELL_001`).

## Value rules

| CygNet point | Result |
| --- | --- |
| No status flag set | quality `Good` |
| `Unreliable` set | quality `Bad` |
| Any other status flag set | quality `Uncertain` (a module accepts it only when it allows uncertain quality) |
| Value not numeric, no timestamp, or no record | refused (`CYGNET_VALUE_NOT_NUMERIC`, `CYGNET_TIMESTAMP_INVALID`, `CYGNET_POINT_NOT_FOUND`) |

The timestamp is the point value's own timestamp converted to UTC; `RetrievedAtUtc` is when the Gateway read it, and
`TimestampKind` is left empty. Whether a value is fresh enough is decided by the host from the module's maximum age.

Failures carry a stable code and a fixed sentence. Whatever CygNet said stays in the inner exception inside the Gateway.
Codes: `CYGNET_QUANTITY_UNSUPPORTED`, `CYGNET_FACILITY_INVALID`, `CYGNET_READ_TIMEOUT` (10 s), `CYGNET_POINT_NOT_FOUND`,
`CYGNET_VALUE_NOT_NUMERIC`, `CYGNET_TIMESTAMP_INVALID`, `CYGNET_READ_FAILED`.

## Status

Compiled on a CygNet host as a proof of concept; availability confirmed on USDCFORAPPDV001 (domain 22567, site WSANET).
This port has not been compiled or run against live points: the CygNet libraries are needed for that. Check on a live
point: that the value, unit and quality match CygNet, which status flags occur on healthy points, and that the
timestamp matches CygNet's UTC time (a CygNet `DateTime` of unspecified kind would be read as local time).


## Phase 2 historian candidate

The Phase 2 branch adds a native historian candidate and licensed-host probe. It does not implement the capability
interface and is not advertised or executed by the live Gateway. Its opt-in native build needs both historian
assemblies in addition to the current/discovery libraries. The normal solution build does not compile this candidate.
See [the host validation guide](../../../docs/implementation/cygnet-history-phase2.md) before compiling or deploying
the native project locally. Sri owns this local validation; no licensed SDK installation is expected in hosted CI.
Native SDK compatibility and live source semantics remain unverified until the local checks pass. This does not block
merging the candidate and probe, but history capability activation still requires verified native behavior.
