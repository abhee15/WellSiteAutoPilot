# CygNet and WAMI integration research

Status: research notes for roadmap step 8, written 2026-10-18. Nothing here is a decision; the choices
below need owner confirmation against a real system. This follows ADR-005 (non-invasive integration):
use only interfaces the providers already support.

Target: **CygNet 9.7 and above**, via Bridge API and via direct access. Sources are the public Weatherford CygNet documentation (links at the end).

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

## 1a. Phase 1 may require direct CygNet access (owner, 2026-10-18)

The owner expects phase 1 may need to talk to CygNet **directly rather than through the Bridge API**, so
the direct path must be designed and experimented with, not left as a fallback. The supported direct
interface is the CygNet .NET API (or COM), which changes the shape of the design:

- The .NET API is a set of **.NET Framework** assemblies with 32/64-bit variants, installed with CygNet. A
  .NET 10 process should not load them: bitness handling, Framework-only dependencies and CygNet's own
  installation layout make that fragile, and CygNet's redistribution terms are unconfirmed.
- Proposed shape: a small **CygNet Native Agent**, a separate .NET Framework 4.8 Windows process that runs
  on a machine with the CygNet client libraries installed (the CygNet host or a client PC). It is our own
  code, uses only supported CygNet interfaces (ADR-005), and exposes the same provider contracts to the
  Integration Gateway over a narrow local channel (named pipe or net.tcp, Windows authentication). The
  Gateway talks only to that channel; it never loads CygNet assemblies.
- The adapter in the Gateway is therefore the same contract either way. **Bridge and direct are two
  transports behind one CygNet adapter**, chosen per deployment by configuration. This keeps core code,
  modules and the contract tests unchanged and lets us compare the two on the owner's system.
- Direct access runs with a Windows identity that holds CygNet ACS permissions, so the agent's service
  account and its permissions become part of installer and security documentation.
- Direct access has no Bridge license tiers, but CygNet's own ACS permissions still gate reads and any
  command. The control path remains blocked on roadmap step 9 either way.
- CygNet's .NET API documentation does not state the connection model (domain, site, service), supported
  Framework versions, authentication or remote use. These must be taken from the help file
  `CygNet.API.chm` in the CygNet `\Bin` folder and confirmed by experiment.

Experiments to run on the owner's box, in order: (1) from a small .NET Framework console app on the CygNet
host, read one current value, one history range and the facility list; (2) repeat from a different machine
with only the client libraries; (3) run the same read from a service account; (4) compare latency and
failure behaviour with the Bridge API if Bridge is available. Findings decide the transport defaults.

## 1b. CygNet must know who the user is (owner, 2026-10-19)

**Requirement.** Actions in CygNet must be performed as, or at least attributed by CygNet to, the signed-in
person. A shared service identity is not enough. This is recorded as an open requirement; it is
deliberately not designed yet and must be settled before step 8 (adapters) and step 9 (control path).

**Why it is not simple here.** The browser signs in to the Server with Negotiate, so the Server knows the
user for the length of that request. But work reaches CygNet asynchronously (Server, NATS, Gateway or Native
Agent), long after the request, and scheduled or autonomous runs have no signed-in user at all. A user's live
token cannot be queued.

**Options to evaluate (none chosen).**

| Option | What it needs | Weakness |
| --- | --- | --- |
| Kerberos constrained delegation with protocol transition (S4U2Self then S4U2Proxy) | A domain or managed service account for the Gateway or Native Agent, delegation to the CygNet service principal granted by a domain administrator, a Windows process | Needs Active Directory configuration and IT agreement; not possible with `LocalService`, and `NetworkService` only if the machine account is trusted for delegation |
| Impersonate inside the Server while handling the request | Nothing extra | Only for synchronous calls made by the Server itself; cannot serve queued, scheduled or autonomous actions |
| A service identity acts, and the user is recorded as "on behalf of" in our audit and in CygNet's | A CygNet account with the right permissions | CygNet cannot enforce the user's own rights; weaker attribution |
| Per-user CygNet or Bridge login | The user's own credentials | We must never hold user passwords; not acceptable |

**Questions that decide it (for the CygNet box and IT).**
1. Does CygNet authenticate Windows identities, CygNet-native users, or both, and does the API or WAMI accept
   an impersonated or delegated Windows identity?
2. Will IT allow constrained delegation for a service account, and will it provide a managed service account?
3. Does the Bridge support acting for a named user (for example a service token plus an on-behalf-of user)?
4. For actions with no signed-in user, which identity does CygNet expect?

**Consequences already visible.**
- A recommendation that a person approves must carry the approver's verifiable identity on the control action,
  so the executing component can act for that approver. This belongs in the step 9 design.
- Service accounts must become configurable per service (today the bootstrapper fixes them), so the Gateway or
  Native Agent can run as a named domain or managed service account without reinstalling. Secrets for that
  account must be stored the way the database password is (protected file, never a command line).
- The Native Agent (section 1a) is a Windows process, which is where delegation would naturally live.

## 2. Consequences for design

1. **Bridge API transport:** the Gateway needs only `HttpClient`; no CygNet files ship with our installer.
   Requires a Bridge license.
2. **Direct transport (section 1a):** a Native Agent on a machine with CygNet client libraries, behind the
   same adapter. Needed when Bridge is unavailable or direct access proves better. OPC UA stays a later,
   read-only option.
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

0. For direct access: `CygNet.API.chm` (from the CygNet `\Bin` folder), the list of CygNet assemblies and
   their versions, whether the box is 64-bit only, and the Windows identity that has ACS permissions.
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
