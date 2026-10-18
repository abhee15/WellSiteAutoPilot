# Telemetry

Every .NET process (Server, Integration Gateway, .NET Worker) calls `AddWellSiteTelemetry` from the shared
`WellSiteAutoPilot.Telemetry` project. Telemetry is diagnostic only; nothing in the product depends on it.

## What is collected

- **Traces:** ASP.NET Core requests (health probes excluded), outgoing HTTP calls, the NATS client
  (`NATS.Net`) and the product's own source `WellSiteAutoPilot`:
  `outbox publish` (producer), `execution run` (worker, consumer) and `execution result apply`
  (server, consumer).
- **Metrics:** ASP.NET Core, outgoing HTTP and every meter named `WellSiteAutoPilot.*`
  (for example `wsa.execution.result.conflicts`).
- **Resource:** `service.name` (`wellsite-autopilot-server`, `-gateway`, `-worker-dotnet`) and version.

## Trace and correlation ids

- `TraceId` is the technical W3C trace. `CorrelationId` is the business id and stays separate: it is a
  tag (`wsa.correlation_id`) on the request and worker spans, never a replacement for the trace id.
- A request that creates an execution writes its W3C `traceparent` into the message envelope
  (`TraceParent`). The worker continues that trace and copies its own span's `traceparent` into each
  result, so one trace covers request, outbox publish, worker run and result apply. A message without a
  valid `traceparent` starts a new trace; it is never rejected.

## Turning export on

Nothing is exported by default, so an offline runtime makes no outbound connections. To export, set one of:

```text
Telemetry__OtlpEndpoint=http://127.0.0.1:4317
OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317
```

In local development the Aspire dashboard accepts OTLP on that port. Set `Telemetry__Enabled=false` to
register nothing. Technical logs, metrics and traces must never become control dependencies.

## Not yet covered

Database spans (Npgsql), runtime and process metrics, and the Python worker. They are additive.
