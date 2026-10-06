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
