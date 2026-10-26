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

This is a host foundation. Production DI continues to use the built-in resolver
until the next dispatch slice supplies authoritative package identity. Existing
V1/V2 messages remain unchanged. The POC bucket-scanning resolver is not copied
into production. No live provider reads or writes are enabled here.

ExecutionContractV2Tests exercises a real managed fixture archive on Windows and
Linux: exact identity, independent instances, preflight before constructor,
too-new SDK/wrong runtime/wrong manifest/hash, unsafe archive, failed entry
point/identity cleanup, size bounds and cancellation without blocking other
loads. ExecutionDispatchTests verifies the worker's exact-hash NATS source
against real approved publication bytes.
