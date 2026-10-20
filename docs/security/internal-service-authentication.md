# Internal service authentication (Integration Gateway)

The Integration Gateway only answers callers it can identify. This implements decision 2 of
[the connections design](../design/connections-workflow-design.md#124-how-callers-reach-the-gateway-decision-2-researched).

## Callers and rights

| Caller | Presents key from | May call |
| --- | --- | --- |
| `server` | `server.keys.json` (Server account only) | Everything internal: adapter list and validation now; connection test, discovery and import later |
| `worker` | `worker.keys.json` (LocalService) | Current-data reads and the integration list only |

`/health/live` and `/health/ready` stay open so the installer and readiness probes work.

## Wire format

`Authorization: WSA-Service <caller>:<key>`. A missing, malformed, wrong or unknown credential gives 401; a valid
credential for a route the caller may not use gives 403. The key is never logged.

## Storage

The installer (`install-services`) writes, under `ProgramData\Weatherford\WellSite AutoPilot`, one file per
service with an access list limited to SYSTEM, Administrators and that service's account:

- `server.keys.json`: `{"Security":{"ServiceKey":"<key>"}}`
- `worker.keys.json`: the same shape with the worker's key
- `gateway.keys.json`: `Security:Callers:server:Verifier` and `Security:Callers:worker:Verifier`, each the base64
  SHA-256 of the matching key. The Gateway never holds a key, only what can check one.

Existing keys are kept on upgrade. `--rotate-service-keys` regenerates both and the services restart with the new
files. The Gateway refuses to start without both verifiers, and the worker refuses to start without its key.

## Development and tests

Supply the same settings as configuration or environment: `Security__Callers__server__Verifier`,
`Security__Callers__worker__Verifier` for the Gateway and `Security__ServiceKey` for the worker. The Aspire host
generates fresh keys for each local run; CI generates them in the workflow steps.

## What this protects against

The network, other accounts, and the worker reaching the Server's rights. A process running as the same account as a
caller can read that caller's key, and a local administrator can read everything.
