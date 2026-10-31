# CygNet native bridge (Windows x64, opt-in)

The saved-connection adapter supports facility discovery and mapped numeric current-point reads through
WSA's existing authorized read plans. See [implementation and live acceptance](../../../docs/implementation/cygnet-saved-connections.md)
for the POC findings, connection settings, build/deployment commands and owner test checklist.

The portable `Integration.CygNet.Adapter` project is included in the main solution and CI. This native
project requires the existing installed CygNet API libraries and is compiled only when the Gateway
is built with `CYGNET_LIBRARY_PATH` set. It is intentionally outside the portable solution.
No COM registration or CygNet-side code change is introduced.

The older development `GET /api/internal/v1/data/current?providerId=cygnet` route remains opt-in.
It shares the native runtime lane/client cache, requires an explicit `Providers:CygNet:Domain` and
uses `Providers:CygNet:Quantities:<quantity>:Udc/Unit`. It has no ambient-domain fallback.
Workers refuse this route for non-simulator execution inputs. Saved CygNet executions always use
`POST /api/internal/v1/adapters/cygnet/current`, restricted to the authenticated Server caller,
with settings resolved from the pinned, currently authorized saved connection.

Unreliable points remain Bad. Initialized/Updated/data-type bits are neutral; unknown/configurable
bits remain Uncertain. Missing and unspecified-kind timestamps fail closed. Read waits are six seconds;
facility queries have a thirty-second budget (connection tests impose ten seconds). A synchronous
vendor call retains the single native slot after the waiting request leaves. Confirm the installed
API's timestamp and quality semantics on the live host before acceptance.
