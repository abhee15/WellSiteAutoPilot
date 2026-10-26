# WellSiteAutoPilot.Integration.CygNet (POC)

Reads current CygNet point values through the managed `CygNet.API.*` assemblies already installed on the host.
No COM registration and no CygNet install step. The host must be licensed (CAL) on the CygNet domain and able to reach its directory service.

## Build
Set `CYGNET_LIBRARY_PATH` to the folder holding `CygNet.API.*.dll`, `CygNet.Data.*.dll` and the native `Cx*64.dll` files, then build the Gateway:

    $env:CYGNET_LIBRARY_PATH = '<libs>'
    dotnet build src\integration\WellSiteAutoPilot.IntegrationGateway -c Release

Without the variable the Gateway builds exactly as before and has no `cygnet` provider.

## Configure (Gateway)
    Providers__CygNet__Domain = 22567
    Providers__CygNet__Quantities__PumpFillage__Udc = <UDC>
    Providers__CygNet__Quantities__PumpFillage__Unit = %

An unmapped quantity is treated as the UDC itself. Domain falls back to the ambient domain.

## Use
Data-binding provider id `cygnet`; asset external id is a facility tag `SITE.SERVICE::FACILITYID` (e.g. `WSANET.UIS::WELL_001`).
Points flagged `Unreliable` map to quality `Bad`, never `Good`.

## Status
Compiles. Availability confirmed on USDCFORAPPDV001 (domain 22567, site WSANET). Not yet exercised against live points: WSANET currently has no facilities.
