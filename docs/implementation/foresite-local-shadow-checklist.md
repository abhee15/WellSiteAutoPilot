# Local ForeSite Shadow acceptance

This is an owner-run checklist. CI uses synthetic HTTP/engineering responses,
PostgreSQL, NATS and actual package hosting; it does not prove a live ForeSite
measurement's time, units or quality. No provider writes are enabled.

## Evidence required before a successful live run

- Capture a sanitized JSON response from the saved connection's
  `Surveillance/well/{numericWellId}` endpoint, including the Value object and
  PumpFillage/scan/quality fields. Remove addresses, usernames, cookies, tokens,
  credentials and customer-identifying text before sharing it.
- Identify the timestamp belonging to the actual measured value, its timezone/
  offset and its meaning when a device stops updating. Compare two HTTP reads
  without a physical/device update. A timestamp that advances on retrieval alone
  cannot establish measurement freshness.
- Confirm the quality/status field and documented mapping for valid, uncertain,
  bad and missing values. Equal LastScanTime and LastGoodScanTime alone is not a
  verified quality contract. Confirm PumpFillage units and whether percentages
  can validly exceed 100.
- Until those semantics are established, POC-shaped scan evidence is persisted as
  Unknown/UnverifiedScan and rejected. Do not increase age to four hours or relabel
  a scan timestamp as measurement time to force success.

## Deployment and package

1. Pull the verified develop commit; record its SHA. Build/install using the
   repository's existing Windows installer workflow and run database migrations
   with the existing migrator. Do not enable automatic startup migrations.
2. Confirm the Server, Integration Gateway, .NET worker and authenticated NATS
   service are running. Keep service caller keys and the broker token private.
3. Verify the Gateway's Windows service account has the intended ForeSite login
   and only the expected source access. Use the saved connection's Test action;
   inspect its acting-as identity and current revision.
4. Use `modules/authoring/README.md` to validate and package the .NET example.
   Preserve reviewed manifest, module ID/version, archive SHA-256 and SDK minimum.
   Upload unchanged bytes in Logic Modules. Review the displayed checksum and
   requirements, then approve through the existing reasoned approval controls.
   Upload is not approval or proof of delivery. Do not use Python for this run.
5. Open the module's existing catalog detail and refresh **Package delivery and
   runtime**. Confirm archive stored, delivery recorded, exact artifact available,
   authenticated worker ready, Gateway reachable and runtime/SDK compatible.
   Record the observation time. These checks do not authorize a selected source.
   A failed delivery remains in the durable outbox with attempt count, safe
   failure code and next retry time; fix broker/storage and allow retry.
   Dispatch before recorded delivery must return MODULE_PACKAGE_NOT_PUBLISHED.
   Recover the same request key after delivery; do not overwrite an existing version.

## Authorized source and configuration

6. Select an existing imported leaf well. Reuse the saved hierarchy; do not
   rediscover/import it just to create this test. Record local AssetId, saved
   ConnectionId/revision, SourceBindingId and numeric external well ID privately.
7. Configure the approved .NET module through the existing editor. Choose the
   single well directly or an existing compatible View. Use ForeSite as provider,
   its exact stored source ID and an empty provider mapping (`{}`). Check the
   displayed connection. No URLs or Windows credentials belong in module inputs.
8. Save, validate and activate the immutable Shadow revision. Record the revision
   and package version/hash. Manual Shadow is the supported live read path;
   scheduled live reads require a separate service-account authority design.
9. Request a Shadow run using the confirmation control. If the response is lost,
   recover the same persisted request key; do not create a second key blindly.
10. Inspect Requested/Running/terminal status in existing history/detail. For a
    successful verified measurement, compare the stored value/unit/measurement
    time/quality with ForeSite and the physical update. Verify package identity,
    source connection, correlation and configuration revision. Confirm no control
    intent was written to ForeSite and no device value changed.

## Failure and recovery cases

11. Use two saved connections with the same external numeric well ID. Prove their
    local Assets/read plans and observed values stay distinct. Include a virtual
    directory in one address and verify the path prefix is retained.
12. Expire the ForeSite session. Concurrent reads should refresh one serialized
    connection session, preserve cookies and retry each operation at most once.
    Reject credentials, cookies or FullErrorMessage text appearing in worker data.
13. Exercise null/missing/non-numeric values, bad/unknown quality, stale and future
    times, absent timezone offset, invalid JSON/login HTML, denied access,
    unavailable provider and timeout. Expect a typed failure and bounded input
    diagnostics; missing values must not become zero or successful outputs.
14. Queue a run, then disable the connection or change an allowed non-source
    setting/revision, revoke the requesting user's relevant grant, or change the
    active configuration. Subsequent reads must be refused. An already authorized
    in-flight HTTP read may finish; no distributed atomic cancellation is claimed.
15. Restart Server/Gateway/worker before read acquisition. Within the authority
    lifetime, the persisted plan must resolve without Gateway-local preparation;
    expired plans require a fresh authenticated request. Direct-user authority is
    bounded to five minutes; Windows proof retains its original one-minute limit.
16. Interrupt a package execution and restart the worker. On first valid package
    load, only recognized unlocked extraction leases may be reconciled. Preserve
    active workers, legacy/unowned directories and reparse points. Verify a fresh
    run extracts reviewed bytes and no previous writable payload is reused.
17. Replay terminal/result messages. The canonical outcome must remain fixed;
    Inbox replay is idempotent and competing terminal outcomes are audited. Treat
    interrupted worker execution as at-least-once work, not exactly-once algorithm
    execution. Do not infer a successful run merely from package approval.

## Record the result

For each case retain build SHA, package ID/version/hash, request/execution ID,
configuration revision, source/connection revision, expected and actual typed
outcomes, sanitized observation times/quality and proof of zero writes. Report
any field-semantic discrepancy before changing parser policy. Persisted audit
history is retained; do not delete it as test cleanup.

## Run the measurement evidence probe first

Use the [probe-first plan](../design/foresite-measurement-evidence-plan.md) before
changing field mappings. Run locally on Windows under the same account as the
Gateway, using the address and positive numeric well ID from the existing saved
connection/source binding. The CLI is an operator diagnostic; it does not prove
application authorization or execution readiness.

From a checkout of this build (PowerShell):

```powershell
# Use the actual saved address privately; never include it in shared reports.
$env:FORESITE_BASE_URL = 'https://YOUR-FORESITE-SERVER/EXISTING-VIRTUAL-DIRECTORY'
dotnet run --project tools/WellSiteAutoPilot.Cli --configuration Release -- foresite probe 42 .\steady.json 6 10
Remove-Item Env:FORESITE_BASE_URL
```

Replace `42` with that binding's actual numeric well ID. For an installed CLI,
use `WellSiteAutoPilot.Cli.exe foresite probe ...` with the same arguments. A new
`.json` report is required; existing files are never overwritten. Defaults are
three samples five seconds apart; limits are 2–12 samples, 1–30 seconds apart,
10 seconds per observation and 180 seconds total. Ctrl+C cancels acquisition.
Exit 0 means the observations were captured, not that live execution is safe.
Exit 2 can still leave a sanitized report containing typed acquisition failures;
exit 130 means cancellation. Inspect `captureOutcome` and each sample's `outcome`.

The report excludes source identity, URL, raw response, account, headers/cookies,
FullErrorMessage and arbitrary properties/text. It retains only fixed numeric,
timestamp, quality and unit candidates with explicit missing/type/offset facts.
Every report says `measurementSemantics: Unverified` and
`authorizesExecution: false`. Recognized quality labels and numeric status codes
are observations, not a verified Good-quality mapping. The probe never increases
freshness limits and never issues scan, configuration or control operations.

Capture separate reports for a stable well and an independently observed normal
update. If available, also capture wells **already** known stale or bad/uncertain;
do not stop equipment or alter its configuration to manufacture a scenario.
Record actual update times, source timezone and unit/quality definitions privately
using the plan's scenario matrix. Review the sanitized report before sharing it.
If the needed field is absent from the fixed candidate allowlist, review its name
and type locally so the allowlist can be extended explicitly; do not share the
entire raw Value object. Repeated GETs alone cannot prove timestamp semantics.

Once those reports and independent definitions establish the mapping, add the
reviewed parser fixtures and run the saved-connection Shadow cases above. Until
then, `EXECUTION_INPUT_TIMESTAMP_UNVERIFIED` remains the expected safe outcome.
