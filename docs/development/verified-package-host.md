# Verified .NET package host

The host takes exact module ID/version, archive SHA-256 and normalized reviewed
manifest JSON. These expectations must come from an authorized Server execution
snapshot. NATS membership is not approval. It requests only `sha256/<hash>` from
`WSA_MODULES`, with cancellation, a 30-second download deadline and the same
16 MiB archive limit as Server upload. Shared package inspection and host SDK,
runtime, identity and reviewed-manifest checks run before extraction or loading.

Each load creates one module instance and a collectible load context. Managed
root-level dependencies load from the package; the SDK comes from the worker.
Native dependencies are unsupported. Managed images load from streams so
working files can be deleted on Windows when the returned lease is disposed.
Failure paths clean partial extraction. This initial host downloads each run;
a persistent disk cache and resource/process isolation are later work.

Hosted executions use `wsa.execution.requested.v3`. The Server locks the approved
catalog version and snapshots its checksum and normalized manifest alongside the
existing V2 execution context, atomically with execution/outbox persistence.
V1/V2 retain the built-in path and never resolve a hosted module by ID/version.
Metadata-only registrations retain V2 behavior. The configured worker consumer
handles V2/V3 through the same execution/result path; started/completed/failed
messages retain their unchanged V2 schema. Manual receipts, scoped reads and
recommendation decisions recognize V3 using the persisted execution Asset scope.

Retirement prevents new package requests. An execution accepted and committed
before retirement may finish using its pinned reviewed bytes. This is the
explicit dispatch acceptance boundary; this slice does not implement remote
cancellation/revocation of already accepted runs. Publication retains retired
artifacts so accepted requests can resolve them. Package-publication delay can
still produce a controlled missing-package failure; worker readiness/retry policy
is separate work.

The worker's working directory defaults to the service account's LocalApplicationData
under Weatherford/WellSiteAutoPilot/module-work; Runtime:ModuleWorkingDirectory
can select an account-owned directory. There is no new service or database schema.
The POC bucket-scanning resolver is not copied into production. ForeSite
connection-aware routing, measurement provenance and provider writes remain
separate slices.

ExecutionContractV2Tests exercises a real managed fixture archive on Windows and
Linux: exact identity, independent instances, preflight before constructor,
too-new SDK/wrong runtime/wrong manifest/hash, unsafe archive, failed entry
point/identity cleanup, size bounds and cancellation without blocking other
loads. ExecutionDispatchTests verifies the worker's exact-hash NATS source
against real approved publication bytes and executes an uploaded/approved managed
archive through real PostgreSQL/outbox/NATS, worker input acquisition and persisted
V2 results. It verifies refusal before approval/after retirement, accepted-request
completion after retirement, an unrelated corrupt object, Running/Completed state
and cleanup. Provider I/O is a deterministic HTTP substitute; on-premises ForeSite
evidence remains a separate live gate.
