# Catalog package upload

The existing Logic Modules catalog now sends the original .wsamodule/.zip File
through the generated uploadLogicModulePackage client and existing binary upload
endpoint. Module managers may choose an archive up to 16 MiB. No package bytes,
manifest, trust flag, or configuration model is rewritten in the browser.

A verified receipt selects the stored version after catalog refresh. Existing
ModuleApprovalControls remain the only approval/retirement interaction. Immutable
version errors ask for a new version; unknown network outcomes ask for catalog
reload. The module detail displays the authoritative package SHA-256 and explicitly
distinguishes approval from delivery/runtime readiness.

The Python audit found no production subscriber/package host/read client. Python
packages remain authorable/uploadable/reviewable, but cannot be selected in setup
or dispatched to the .NET package worker. The backend enforces that boundary; it
is not a frontend-only permission/capability check. Metadata-only V2 compatibility
behavior is preserved.

Tests cover manager visibility, unchanged File identity/credentials, 16 MiB
rejection, immutable-version errors and server-detail exclusion. A PostgreSQL
regression verifies approved Python package dispatch is refused with a typed
runtime-unsupported outcome. Generated clients are regenerated from OpenAPI for
builds and are not hand-edited or checked in.
