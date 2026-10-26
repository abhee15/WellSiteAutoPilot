# Logic Module package format

WellSite AutoPilot uses the `.wsamodule` extension for immutable Logic Module packages.

A V1 package is a ZIP-compatible archive with this layout:

```text
manifest.json
payload/
  <runtime artifacts>
```

The manifest is the package source of truth and must pass the same semantic validation used by the Server and CLI. Payload content is runtime-specific and may contain compiled .NET artifacts, Python source/wheels, or other approved module resources.

The CLI command

```text
wsa module pack <manifest.json> <payload-directory> <output.wsamodule>
```

creates a deterministic archive by sorting payload paths and normalizing ZIP entry timestamps. It prints the resulting SHA-256 digest. The catalog treats a `ModuleId + Version` pair as immutable: the same version may be registered again only when package content has the same SHA-256.

Package signing and trusted-publisher verification are a separate security step. Creating a package or computing its checksum does not make it trusted. Browser/API imports are therefore cataloged as untrusted until a governed trust workflow verifies or approves the publisher.

## Inspection and host preflight

`LogicModulePackageReader` is the shared CLI/application inspection boundary. It
reads a bounded snapshot, hashes those exact archive bytes and checks the manifest
through the same reader used by CLI validate/pack. It reads all payload entries
without extracting files or executing code. Inspection limits remain 256 MiB
compressed, 512 MiB declared expanded content, 1 MiB manifest and 10,000 entries.
Noncanonical archive paths, duplicate names (case insensitive), file/directory
conflicts, symbolic links and Windows-unsafe names are rejected. Packages use the
canonical `manifest.json` and `payload/` casing emitted by the product packer.
Pack inspects the finished temporary archive before replacing an existing output.

An author can check the package against a declared target host:

```text
wsa module preflight example.wsamodule weatherford.foresite-well-input-check.dotnet 1.0.1 EXPECTED_SHA256 DotNet 1.1.0
```

Replace `EXPECTED_SHA256` with the previously reviewed archive checksum. The
expected module ID/version and checksum must eventually come from the authorized
catalog/execution record, not the archive itself. `DotNet` or `Python` identifies
the target runtime; the final argument is that host's supported module SDK version,
not the .NET or Python runtime version. The CLI checks supplied host information;
it does not contact a worker or discover installed versions.

A mismatch returns exit code 2 and a stable failure code: `MODULE_PACKAGE_HASH_MISMATCH`,
`MODULE_PACKAGE_IDENTITY_MISMATCH`, `MODULE_RUNTIME_INCOMPATIBLE` or
`MODULE_SDK_INCOMPATIBLE`. Invalid expected checksums/host versions and malformed
archives are controlled failures too. `module verify` retains structural-only
inspection when no host expectation is supplied.

SDK comparison follows SemVer precedence: prereleases sort below their release,
numeric prerelease identifiers sort numerically and build metadata is ignored.
Module identity/version still match exactly, including version build metadata,
because they identify immutable catalog content. Module versions must be valid SemVer strings of at most 64 characters (the catalog
column bound); minimum/host SDK versions permit at most 256 characters. Display
names and publishers permit at most 256 characters. Required manifest fields
cannot be omitted or null; nullable requirement fields retain their optional
semantics. Runtime/profile enums use names rather than
JSON integers in package/standalone authoring manifests.

Inspection/preflight does not approve a publisher, verify a signature, resolve
runtime dependencies, locate an entry-point type or install/load a package. No
Server upload/distribution, worker resolver or production Python host is enabled
by this slice. A future host must use the shared preflight against its authoritative
request and retain the existing authorization/approval boundary before loading.

## Server package upload

`POST /api/v1/logic-modules/packages` accepts the raw archive body with
`Content-Type: application/octet-stream`. The first server upload slice permits
at most 16 MiB per archive, including streamed bodies without Content-Length.
Offline CLI inspection retains its larger limit. The endpoint requires an
active deployment-wide `logic.manage` grant; ExactAssets/AllAssets grants cannot
supply this authority. It returns the existing module metadata response (201),
including the server-computed checksum. It accepts no caller-supplied trust flag,
checksum or filename. Other media types return 415; invalid/oversized packages
return a structured validation error.

The server inspects a bounded snapshot without extracting or executing payloads.
It stores the archive unchanged in `logic.module_packages` (`bytea`, SHA-256 key)
and registers a disabled `Untrusted` catalog version in one serializable
transaction. Registration and package-upload audit events commit with the bytes.
A failed write leaves none of those partial records. Backup must include this
table. The EF-generated `AddLogicModulePackages` migration is applied by the
existing explicit migrator/installer process, not normal server startup.

Uploading identical bytes again is idempotent and preserves approval/retirement.
Different content for the same module ID/version is rejected. A previously
approved metadata-only version cannot acquire package bytes after review; upload
a new version so the bytes can be reviewed first. Concurrent inserts may return
`MODULE_CATALOG_CONFLICT`; a retry with the same immutable bytes is safe.

Package metadata remains readable through the existing catalog routes. There is
no general archive download endpoint. Upload itself does not publish bytes. Approval-backed publication is described
below; worker loading remains a separate slice.

## Approval-backed publication

Approving a version with stored package bytes now commits the approval, its audit
record and a `module-package.publication.v1` work item in the existing database
outbox as one transaction. Upload, repeated approval and retirement do not enqueue
extra publication requests. The catalog version ID is also its single publication
ID. Repeat approval can queue a missing request for a package approved by a prior
release, with a separate audit event and no repeated trust-change audit. Existing
built-in/metadata-only versions retain their
approval behavior and do not create work with missing package bytes.

The Server runs a separate package dispatcher; the execution dispatcher excludes
these internal work items. The package dispatcher rechecks catalog ID/version/hash
and current approved/enabled state, hashes the stored bytes and publishes them to
the NATS Object Store `WSA_MODULES`, named `sha256/<approved-checksum>`. It requires
file storage without expiry or eviction of old objects. It does not extract or
execute code. No package request is broadcast as an execution message.

A publication row is locked while being handled, so competing dispatchers skip the
same row. A shared module-row lock fences retirement: a retirement committed first
causes publication to be skipped and audited; retirement that starts during a
publication waits for its completion. Already published retired artifacts are kept
for reproducible history. A future worker must still validate current execution
authority and the expected package checksum before loading code.

Delivery failures leave the work pending for retry and do not roll back the already
committed approval. Broker operations have a 30-second deadline. Broker success
followed by a database failure can cause a repeat write of the same immutable bytes;
the processed marker and system publication audit commit together, so the audit
is recorded once. Approval and publication preserve correlation/trace context.

An approved flag is a trust decision, not proof that a package has reached a worker.
Publication has no effect on the built-in resolver or the Python worker scaffold.
Cache management, package loading/isolation and execution-request package identity
remain subsequent work. NATS data-volume loss after successful delivery requires
recovery/republication; the outbox retry protects pending delivery, and is not an
automatic rebuild of an already processed Object Store.
