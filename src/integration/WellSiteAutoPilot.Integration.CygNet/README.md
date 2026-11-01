# WellSiteAutoPilot.Integration.CygNet (opt-in development provider)

Reads current CygNet point values through the managed `CygNet.API.*` assemblies already installed on a CygNet-licensed
host. No COM registration and no CygNet install step. The host must hold a CygNet CAL and be able to reach the domain's
directory service.

## What it is, and what it is not

- A `cygnet` provider on the Integration Gateway's `GET /api/internal/v1/data/current` route, for trying CygNet reads
  on a development machine.
- The live backend of the `cygnet` connection adapter (`src\integration\WellSiteAutoPilot.Integration.CygNet.Adapter`):
  connection test, facility discovery and current reads through authorized read plans. See `docs/implementation/cygnet-adapter.md`.
- Not in `WellSiteAutoPilot.slnx`, so CI and a normal build never need the CygNet libraries. The Gateway compiles the
  provider in only when `CYGNET_LIBRARY_PATH` is set; without it the Gateway builds exactly as before.

## Build

Set `CYGNET_LIBRARY_PATH` to the folder holding `CygNet.API.*.dll`, `CygNet.Data.*.dll` and the native `Cx*64.dll` files,
then build the Gateway (Windows, x64):

    $env:CYGNET_LIBRARY_PATH = '<libs>'
    dotnet build src\integration\WellSiteAutoPilot.IntegrationGateway -c Release

With the variable set the Gateway targets `net10.0-windows`. Unset it again before building the rest of the solution.

## Configure (Gateway)

    Providers__CygNet__Domain = 22567
    Providers__CygNet__Quantities__PumpFillage__Udc = <UDC>
    Providers__CygNet__Quantities__PumpFillage__Unit = %

- `Domain` is optional; without it the host's ambient CygNet domain is used. A value that is not a whole number from
  0 to 65535 stops the Gateway at startup.
- Only quantities listed under `Quantities` can be read, and each needs both `Udc` and `Unit`. Anything else is
  refused with `CYGNET_QUANTITY_UNSUPPORTED`; a quantity name is never used as a UDC.

## Use

Data-binding provider id `cygnet`. The asset external id is a facility tag, `SITE.SERVICE::FACILITYID`
(for example `WSANET.UIS::WELL_001`).

    GET /api/internal/v1/data/current?providerId=cygnet&assetExternalId=WSANET.UIS::WELL_001&quantity=PumpFillage
    Authorization: WSA-Service server:<key>

The Gateway needs a verifier for each internal caller (`Security__Callers__server__Verifier` and
`Security__Callers__worker__Verifier`, the base64 SHA-256 of the key), as for any local Gateway run.

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
