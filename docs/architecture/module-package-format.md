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
because they identify immutable catalog content. Manifest and host versions must
be valid SemVer strings of at most 256 characters. Required manifest fields
cannot be omitted or null; nullable requirement fields retain their optional
semantics. Runtime/profile enums use names rather than
JSON integers in package/standalone authoring manifests.

Inspection/preflight does not approve a publisher, verify a signature, resolve
runtime dependencies, locate an entry-point type or install/load a package. No
Server upload/distribution, worker resolver or production Python host is enabled
by this slice. A future host must use the shared preflight against its authoritative
request and retain the existing authorization/approval boundary before loading.
