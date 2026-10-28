# ForeSite controller acquisition semantics

## Owner-approved contract

The owner clarified the ForeSite/CygNet acquisition contract on 27 October 2026
(America/Chicago). CygNet reads a controller datagroup as one transaction; its returned
register values share the successful scan timestamp. ForeSite exposes those
controller values in Surveillance. LastScanTime is the latest scan issued;
LastGoodScanTime is the latest successful scan that returned values. This
clarification supersedes the earlier assumption that a shared scan timestamp
cannot establish controller acquisition time. It is domain-owner evidence,
not a claim that cloud CI accessed the live installation.

| Logical quantity | ForeSite Surveillance field | Unit | Acquisition time |
| --- | --- | --- | --- |
| PumpFillage | CurrentPumpFillage | % | LastGoodScanTime |
| AveragePumpFillage | AveragePumpFillage | % | LastGoodScanTime |

The existing supported percent range remains 0–100 inclusive. Missing, null,
nonnumeric and out-of-range values are rejected. Time must carry Z or an explicit
offset and is normalized to UTC. Missing, invalid or default LastGoodScanTime is
rejected even if LastScanTime or a well-test date exists. No fallback uses retrieval
time, latest scan-attempt time or a trend sample.

Good quality means successful acquisition of a valid value under this controller
contract; it is not independent sensor calibration/validity evidence. It does not
mean fresh. The host separately enforces each reviewed module's maximum age and
future-time policy. A later failed scan does not change the timestamp or quality
of previously acquired values; their increasing age can make them unusable.
The adapter does not infer a per-point quality enum from CommStatus or from
equality of the two scan timestamps.

EngineeringValue records source foresite:controller-scan, timestamp kind
ControllerScan and a separate retrieval time. Bounded failure diagnostics preserve
that provenance through worker messages, PostgreSQL and the existing detail UI.
Historical Unknown/UnverifiedScan evidence remains unchanged and readable.

## Integration boundary

Use ForeSite field names. The customer-specific CygNet UDC mapping belongs to
ForeSite and is not configuration that AutoPilot modules or workers must know.
Current reads retain GET Surveillance/well/{id} via the saved connection/session.
PointFieldList/{wellType} and CygNetTrends are useful for future field discovery
and historical requirements; this change introduces no historian polling or
assumption that a trend's latest sample equals the current controller value.

A well-test field must use its corresponding source date, for example a confirmed
LatestValidWellTestDate association, never LastGoodScanTime. WaterCut and other
well-test quantities remain unsupported by the current two-quantity live-read
allowlist; adding them requires an explicit field/date/unit mapping and tests.
Their presence in the Surveillance response does not opt them into this rule.

## Validation and deployment

Adapter regressions cover both fields, inclusive numeric boundaries, explicit UTC
offsets, missing/invalid scan time, later failed scan attempts, ignored site UDCs,
well-test separation, connection isolation, token/cookie refresh and GET-only calls.
The hosted-package suite exercises fresh success, stale/future acquisition,
missing value and invalid timestamp through authenticated NATS, real package
hosting and PostgreSQL, with the external HTTP service substituted. Existing
source revocation/disabled connection, synthetic success and historical diagnostic
coverage remain.

No API shape, database migration, installer setting, scheduled-live authority or
provider write is added. Run the saved-connection manual Shadow acceptance in
foresite-local-shadow-checklist.md on the actual Windows installation after CI.
Record build/package/configuration/source revisions and compare the current
values and LastGoodScanTime. Partial datagroup or unexpected point validity
behavior must be investigated rather than hidden by widening freshness limits.
