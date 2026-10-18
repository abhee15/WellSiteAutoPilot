# CygNet and WAMI integration research

Status: research notes for roadmap step 8, written 2026-10-18. Nothing here is a decision; the choices
below need owner confirmation against a real system. This follows ADR-005 (non-invasive integration):
use only interfaces the providers already support.

Target: **CygNet 9.7 and above.** Sources are the public Weatherford CygNet documentation (links at the end).

## 1. CygNet interfaces that exist

| Interface | Protocol and reach | Fit for the Gateway (.NET 10) |
| --- | --- | --- |
| **CygNet Bridge API** | REST over HTTP(S), served by CygNet Bridge in IIS. Separately licensed | Best fit. Plain HTTP works from any runtime, remote, no CygNet assemblies |
| **.NET API** (`CygNet.API.*`) | .NET Framework assemblies from the CygNet `\Bin` folder, 32 and 64 bit variants, needs a CygNet installation on the host | Poor fit. Targets .NET Framework, ties the Gateway to CygNet files, and redistribution terms are unknown |
| **COM API** | 32/64-bit COM libraries; documented for VBScript, which is deprecated since October 2023 | Not suitable |
| **OPC UA Server** | Windows service that itself calls the Bridge API; read-only data (real-time, history, facilities, points, alarms) | Possible later for read-only. Adds a component and a license |

### Bridge API facts from the documentation

- Version pairing: **CygNet Bridge v4.6 for CygNet 9.7, v4.7 for 9.8.** The page for 9.7 also states CygNet
  Software 8.5.1 or later.
- Authentication: call `clientloginapi/api/login` for a token, send it in `X-WFT-AuthToken`, and send the
  domain id in `X-WFT-CygNetDomain` on every request. The token lasts 7 days; an expired token gives 401 and
  the client logs in again. Two-factor authentication is optional. Access follows the user's CygNet ACS
  permissions plus web server domain permissions. HTTPS is strongly recommended.
- Data areas: Access, Alarms, Devices, Facilities, Measurement Groups, History, Notes, Points, Real-time,
  Services, Tables.
- **License tiers decide what is possible:** Base covers everything except alarms and control. The **Alarm**
  tier adds acknowledge and clear. The **Control** tier adds device commands, group polling, DEID and point
  value sends.
- Interactive API help is served by the deployed system at `/CygNet/Help`. The public pages do **not** list
  endpoint paths or request and response shapes, so those must come from the customer's system.

### Mapping to our provider contracts

| Contract | Bridge API area | Needs |
| --- | --- | --- |
| `IProviderHealthProvider`, `IProviderMetadataProvider` | Services (service status), login result | Base |
| `ICurrentDataProvider` | Real-time (value, units, quality) | Base |
| `IHistoricalDataProvider` | History | Base |
| `IAssetDiscoveryProvider` | Facilities, Points, Measurement Groups | Base |
| `IAlarmProvider` (read) | Alarms | Base for read |
| `ICommandProvider` | Control tier calls | **Control license**, plus control design sign-off (roadmap step 9) |
| `IVerificationProvider` | Real-time readback | Base |

Read-only Shadow work needs only the Base tier. Control stays out until step 9 is approved.

## 2. Consequences for design

1. **Use the Bridge API for CygNet** unless the customer has no Bridge license. The Gateway then needs only
   `HttpClient`; no CygNet files ship with our installer.
2. **If a customer has no Bridge license** the options are a separate .NET Framework helper process using
   the CygNet .NET API on a CygNet host, or OPC UA. Both are heavier; treat as a reported integration gap
   (ADR-005) until a customer needs it.
3. **Compatibility check:** report Bridge version vs CygNet version from the descriptor, and mark
   `ProviderCompatibility` `Incompatible` when the pairing above is violated.
4. **Token handling:** cache the token per domain, re-login once on 401, never log it, store the Bridge
   credentials as a protected secret in the Gateway configuration.
5. **Units and quality:** map CygNet real-time quality and units to `EngineeringValue.Quality` and `Unit`
   explicitly; unknown quality is `Unknown`, never good.
6. Facility and point identifiers (site, service, facility id, UDC) become the provider's `ExternalId`; the
   exact format is decided once endpoint shapes are known.

## 3. WAMI (from the owner's description)

Known: WAMI is a WCF service over **net.tcp**. A server process spawns several worker processes (COM based
calculations) and routes each request to a worker. AutoPilot calls only the service contract and never the
workers (ADR-005).

Design implications that follow from this description:

- The client is a WCF `NetTcpBinding` client in the Gateway (`System.ServiceModel.NetTcp` client packages
  work on .NET 10), so the Gateway should run on Windows when the binding uses Windows transport security.
- Worker-based execution means **calls can be slow, queued or fail when the pool is busy.** The adapter needs
  per-call timeouts, bounded concurrency per WAMI server and clear error codes (`WAMI_TIMEOUT`,
  `WAMI_BUSY`) rather than blind retries. Calculations must be idempotent from our side; a retry after a
  timeout may run the calculation twice.
- Calculation calls map to `ICalculationProvider.CalculateAsync`; inputs and outputs are name to decimal
  with units resolved in the adapter, not in modules.

### Needed from the WAMI box (all non-sensitive)

1. The service contract: the interface and data contract types, or WSDL / MEX output
   (`svcutil` or `dotnet-svcutil` against the metadata endpoint, if metadata is enabled).
2. Endpoint address and the binding settings: security mode (None, Transport, Message), client credential
   type (Windows, certificate), message size limits, timeouts.
3. The set of calculations to call, with example input and output values and units.
4. Behaviour when all workers are busy: queue, fault, or timeout, and typical call duration.
5. WAMI version and whether the contract differs between versions.

## 4. What I need from the CygNet box

1. CygNet version and CygNet Bridge version, and whether the **Bridge API** option is installed.
2. License tiers present (Base, Alarm, Control).
3. The Bridge API help page (`https://<host>/CygNet/Help`) saved as a file, or its OpenAPI/JSON if offered.
4. A **test** CygNet domain, a read-only user, and one facility with a few points (current and history).
5. Whether HTTPS and two-factor authentication are enabled on Bridge.

With items 1 to 4 the next step is a Bridge API client behind `ICurrentDataProvider` and the health and
metadata contracts, tested against a recorded-response fake, with the Provider Simulator unchanged.

## Sources

- [CygNet Bridge API (9.7)](https://softwaredocs.weatherford.com/cygnet/97/Content/Topics/CygNet%20Bridge%20API/CygNet%20Bridge%20API.htm)
- [CygNet Bridge API (9.8)](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/CygNet%20Bridge%20API/CygNet%20Bridge%20API.htm)
- [CygNet .NET API (9.8)](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/Scripting/CygNet%20.NET%20API.htm)
- [CygNet COM API (9.8)](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/Scripting/CygNet%20COM%20API.htm)
- [CygNet OPC UA Server overview (9.8)](https://softwaredocs.weatherford.com/cygnet/98/Content/Topics/OPC_UA/CygNet_OPCUA_Server_Overview.htm)
- [Now introducing REST APIs (2018 blog)](https://www.weatherford.com/blog/cygnet/november-2018/now-introducing-–-rest-apis/)
