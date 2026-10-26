# ADR-007: Module package storage and distribution to workers

## Status

**Accepted for the owner-directed server storage and approval-backed publication slices.**
Worker loading/isolation remains a separate implementation decision. The Object
Store prerequisite below was verified before publication was enabled.

## Context

Registration today records a manifest and a checksum only. No package is uploaded, stored or loaded, and
the .NET worker resolves one built-in sample. Workers must not read business tables (ADR-001), do not call
the Server over HTTP, and the product runs on premises, normally without Internet.

## Decision

1. **The Server owns packages.** An uploaded `.wsamodule` is unpacked, its manifest is checked by the
   existing validator, and the immutable package is stored in a `logic` schema table (`bytea`) keyed by
   its SHA-256. The module is registered `Untrusted`. A stored package is never edited.
2. **Workers receive packages through the JetStream Object Store**, bucket `WSA_MODULES`, object name
   `sha256/<hex>`. Workers already hold NATS connections and credentials, so no new channel or credential
   is introduced, and it works offline.
3. **Approval durably queues publication; the Server publishes only approved packages**, never at upload. An
   unapproved package is therefore never available to a worker. Retiring a module does not delete the
   object (running history must stay reproducible); the worker refuses to start a retired version because
   the execution request names the version and the Server will not queue one.
4. **Workers verify before use.** A worker pulls by SHA-256, recomputes the hash, rejects a mismatch,
   caches on local disk by hash and loads from the cache. A cache entry is trusted only after the hash was
   verified when it was written.
5. **Keying by hash is the extension point.** Signatures, trusted publishers and retention (roadmap
   step 10) add metadata beside the hash and move nothing.
6. **Authorization:** upload and approval require `logic.manage` (deployment-wide). Reads of module
   metadata keep `logic.read`. Package bytes are never returned through a general read endpoint.

## Consequences

- Backup of the database includes every package, so a restore needs no external file store.
- The Object Store adds storage use on the NATS volume; size limits per package are set at upload.
- Loading code from a package is a trust boundary. Isolation of the loaded module (load context, process
  or both) is decided in the worker-loader step, not here.

## Open item

Verified by the merged upload/storage slice: NATS.Net 3.3.0 with bundled
nats-server 2.15.0 creates the bucket, round-trips multi-chunk content and retains
it after a server restart without re-upload. The approval-publication integration
checks also exercise real package bytes, retries and retirement fencing.

## Implementation evidence

The package-upload slice stores bounded immutable archive bytes with untrusted
catalog registration in one authorized transaction. Approval-backed distribution now uses the existing outbox with a separate Server
publisher; execution dispatch does not consume package work items. The upload route and schema are documented
in [module-package-format.md](../architecture/module-package-format.md#server-package-upload).

The NATS integration test now covers a multi-chunk object keyed by its SHA-256,
repeat publication, an actual server restart, and retrieval from the existing
bucket without re-creating it or uploading content again. This must pass against
the pinned NATS client/server before distribution relies on the Object Store.
The CI restart verifies process/disk persistence, not survival of hardware or
volume loss. Worker loading and its isolation/trust boundary remain separate from publication.
See the package-format document for transactional delivery and recovery limits.
