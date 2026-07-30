# Operations

The bridge writes structured JSON logs to standard output outside the
`Development` environment. Development uses a concise single-line console
format. Every inbound response includes an `X-Correlation-ID`; a caller value
is reused only when it is a bounded visible identifier, otherwise a fresh value
is generated.

The bridge emits bounded request count and duration metrics and bridge-owned
request spans. It does not put query strings, request bodies, OAuth material,
cookies, configured header values, or client credentials into diagnostics.
`Bridge:Telemetry:OtlpEndpoint` is optional; when absent no OTLP exporter is
created. When configured it must satisfy the same safe URL validation as other
outbound endpoints.

`/health/live` checks only that the process is running. `/health/ready` checks
the already-validated local startup state and makes no outbound OAuth or MCP
calls. Neither endpoint exposes configuration or credential details.

## Graceful shutdown

A shutdown signal (for example a container orchestrator's `SIGTERM`) stops the
bridge from accepting new requests and gives in-flight requests, including
open MCP streams, up to `Bridge:Limits:ShutdownDrainTimeoutSeconds` to finish
before they are forcibly ended. Choose a drain window at least as long as the
slowest expected MCP tool call or streaming response an operator is willing to
wait for during a rolling deployment; requests still active when the window
elapses are aborted rather than left to block shutdown indefinitely.
