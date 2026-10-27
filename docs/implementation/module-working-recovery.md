# Module working-directory recovery

The existing exact-hash .NET package host now leases each extraction with an
exclusive file beside the working root (`<working-root>.leases`). Marker files
are outside the extraction, so archive entries cannot replace them. Normal
completion and constructor/evaluation failures release the lease after cleanup;
a failed cleanup retains the ownership marker for a later restart.

On its first valid package load, a new host reconciles up to 1,000 marker files.
It deletes only recognized, unlocked GUID-named extractions owned by this host
format. Active runs in another host/process retain exclusive leases. Unknown
or legacy directories without markers, malformed markers, symlinks/reparse
points and inaccessible directories are preserved. The root must be the
service account's protected working directory; permissions are not broadened.
The empty sibling lease directory is expected persistent host metadata, not a
package cache. No package bytes are reused without existing verification.

Tests retain the existing loader suite and add a restart instance with two live
runs, an owned orphan and an unowned directory. Recovery removes only the orphan;
the original module remains executable, other runs stay isolated, and successful
disposal still leaves the extraction root empty. Windows CI exercises the same
file-sharing and cleanup behavior.

This addresses crash-orphan cleanup for new runs. Pre-upgrade unmarked directories
require operator review. Execution delivery remains at least once; package hosting
and working-file cleanup do not promise exactly-once module evaluation or hard
termination of trusted in-process code. Persisted read plans, current grant/
connection/revision checks and terminal result/Inbox/conflict-audit semantics
remain the existing restart/replay boundaries. No provider writes are enabled.
