# Logic Module SDK contract

The Logic Module SDK is the provider-neutral boundary between WellSite AutoPilot runtime workers and customer/Weatherford algorithm code.

A module receives only semantic execution context:

- execution and module identity;
- correlation identity and evaluation time;
- configured parameter JSON;
- role-based Asset context;
- normalized engineering input values with quantity, UOM, timestamp, quality, and source provenance.

A module may return:

- engineering results;
- recommendation intents;
- control intents;
- persistent state updates.

SDK 1.1 adds optional `LogicModuleExecutionResult.FailureCode` without changing
the V1 constructor/interface. Null or absent retains successful evaluation.
A non-null code reports a failed evaluation independently of `OutcomeCode`.
Both .NET engine paths reject it before returning outputs; existing consumers
then publish ExecutionFailed with that code. Failure codes must match
`[A-Z][A-Z0-9_]{0,127}`; malformed codes and null results fail as
`MODULE_RESULT_INVALID`. Failed evaluations never release engineering results,
recommendations, control intents or state updates for completion processing.
No success/failure meaning is inferred from an algorithm's outcome name.
This is execution failure reporting, not an arbitrary-code isolation boundary.

Existing modules can keep the V1 result shape. New modules using FailureCode
declare minimumSdkVersion 1.1.0 and require an updated worker. The SDK project
now carries that explicit version rather than inheriting the product version.
The verified .NET package host enforces the minimum against its actual SDK before
loading. Hosted V3 requests pin the reviewed checksum/manifest; legacy requests
cannot fall back to uploaded code. Python production execution remains separate
work. See [verified package hosting](../development/verified-package-host.md).

The SDK deliberately contains no CygNet, WAMI, PostgreSQL, NATS, HTTP, credential, or worker-host types. Provider access is performed by the runtime before module execution or through separately governed runtime SDK capabilities.

A module never executes a physical control command directly. `ControlIntentDraft` expresses desired behavior only; the Server remains responsible for mode handling, readiness, policy, ownership, durable ControlAction creation, provider execution, and verification.

The V1 interface is named `ILogicModuleV1` so future breaking SDK changes can coexist with installed packages rather than silently changing module semantics.
