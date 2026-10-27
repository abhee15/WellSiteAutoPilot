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
5. Confirm the package publication has completed. A failed delivery remains in the
   durable outbox with attempt count, safe failure code and next retry time; fix
   the broker/storage issue and allow retry. Do not overwrite an existing version.

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
16. Replay terminal/result messages. The canonical outcome must remain fixed;
    Inbox replay is idempotent and competing terminal outcomes are audited. Treat
    interrupted worker execution as at-least-once work, not exactly-once algorithm
    execution. Do not infer a successful run merely from package approval.

## Record the result

For each case retain build SHA, package ID/version/hash, request/execution ID,
configuration revision, source/connection revision, expected and actual typed
outcomes, sanitized observation times/quality and proof of zero writes. Report
any field-semantic discrepancy before changing parser policy. Persisted audit
history is retained; do not delete it as test cleanup.
