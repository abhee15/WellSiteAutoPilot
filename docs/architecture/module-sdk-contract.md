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

The SDK deliberately contains no CygNet, WAMI, PostgreSQL, NATS, HTTP, credential, or worker-host types. Provider access is performed by the runtime before module execution or through separately governed runtime SDK capabilities.

A module never executes a physical control command directly. `ControlIntentDraft` expresses desired behavior only; the Server remains responsible for mode handling, readiness, policy, ownership, durable ControlAction creation, provider execution, and verification.

The V1 interface is named `ILogicModuleV1` so future breaking SDK changes can coexist with installed packages rather than silently changing module semantics.
