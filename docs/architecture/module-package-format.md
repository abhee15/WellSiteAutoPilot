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
