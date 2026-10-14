# NATS and JetStream Messaging Topology

WellSite AutoPilot uses Core NATS only for transient communication and JetStream for durable work/event delivery.

The initial durable topology is:

| Stream | Subjects | Purpose |
| --- | --- | --- |
| `WSA_EXECUTION` | `wsa.execution.>` | execution requests/results/workflow messages |
| `WSA_CONTROL` | `wsa.control.>` | governed ControlAction requests/results |
| `WSA_INTEGRATION` | `wsa.integration.>` | integration health and provider events |

Message contracts remain explicitly versioned in subject names and in the message envelope.

JetStream message IDs are used for broker-level duplicate detection. This complements, but does not replace, application-level Inbox/idempotency handling because redelivery and retry can occur at other boundaries.

Runtime stream retention/size policies and production NATS credentials/TLS are deployment configuration and will be finalized separately. The topology contract itself is tested against a real JetStream-enabled NATS server in CI.

## Execution delivery timing and long-running modules

Request and result consumers share a validated `Messaging:JetStream` configuration in the
.NET Worker and Server:

| Setting | Default | Allowed |
| --- | --- | --- |
| `RequestAckWaitSeconds` | 120 | 10–900 |
| `ResultAckWaitSeconds` | 30 | 10–900 |
| `RequestProgressSeconds` | 20 | 1 to one-third of request AckWait |
| `MaxDeliver` | 5 | 1–20 |

During request handling, both .NET V1/V2 workers send JetStream `+WPI` progress
acknowledgments periodically until module execution and result publication finish.
An unsuccessful heartbeat cancels the in-flight work and prevents the final ACK.
This improves long-running execution reliability during high-latency provider calls.
It does **not** guarantee exactly-once execution: JetStream can redeliver after
crashes, network partitions or stalled processes. Database terminal result
compare-and-set and conflict auditing prevent silent competing outcomes;
durable worker ownership, fencing and controlled redelivery remain follow-up
work before horizontal worker scaling or autonomous physical controls.

`AckWait` governs broker redelivery, **not** module runtime. Keep independent
module execution limits and provider-call timeouts. Changing consumer timing
updates the durable JetStream consumer on the next service reconnect/start.
Incorrect configuration fails at process startup, not after accepting work.
