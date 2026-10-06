# Error and Exception Handling

WellSite AutoPilot uses stable failure contracts at process and API boundaries. Raw implementation exceptions are diagnostic data and are not client contracts.

## Rules

- Expected business conditions use explicit WellSite AutoPilot failure codes.
- Provider and transport exceptions are translated at the boundary into stable failure categories.
- Unexpected exceptions return `UNEXPECTED_ERROR` and never expose the original exception message or stack trace to the browser.
- HTTP error responses use `application/problem+json` with stable code, correlation ID, trace ID, and retryability fields.
- Caller-supplied correlation IDs are bounded and validated; otherwise the platform generates one.
- Technical exception details remain in structured logs correlated by failure code, correlation ID, and trace ID.
- Request cancellation is distinguished from server failure.
- Retryable dependency failures are distinguished from terminal business failures.
- Physical control code must fail toward no new control when an unexpected exception occurs.

## Initial categories

| Kind | HTTP status | Default code |
| --- | ---: | --- |
| Validation | 400 | `VALIDATION_FAILED` |
| Not Found | 404 | `RESOURCE_NOT_FOUND` |
| Conflict | 409 | `CONFLICT` |
| Authorization | 403 | `AUTHORIZATION_DENIED` |
| Dependency Unavailable | 503 | `DEPENDENCY_UNAVAILABLE` |
| Dependency Timeout | 504 | `DEPENDENCY_TIMEOUT` |
| Request Cancelled | 499 | `REQUEST_CANCELLED` |
| Unexpected | 500 | `UNEXPECTED_ERROR` |

Provider-specific codes such as `WAMI_TIMEOUT`, `CYGNET_UNAVAILABLE`, `DATA_STALE`, and `CONTROL_VERIFICATION_FAILED` are layered on this common mechanism.

The UI must make decisions from stable codes and structured fields, never by parsing English error text.
