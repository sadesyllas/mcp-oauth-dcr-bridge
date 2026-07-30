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

## Credential and certificate rotation

Every credential the bridge holds — the upstream client secret, the
`private_key_jwt` signing certificate (see
[certificate-backed authentication](configuration.md#certificate-backed-private_key_jwt)),
and any configured OTLP endpoint — is read once at startup and held only in
process memory; none is cached to disk or reused across a restart. Rotating
any of them is the same two-step operation regardless of type: replace the
configuration value or mounted file, then restart or redeploy the bridge.
There is no in-place reload, and no explicit invalidation step, because the
bridge never persists a copy that would need one. Rolling back a rotation is
the identical operation using the previous value or file.

## Incident response

Because the bridge is stateless and holds no registration, token, session, or
user data, an incident investigation starts and ends with three artifacts,
all already produced under normal operation: the correlation ID from the
affected request (`X-Correlation-ID`), the structured JSON request logs, and
the bridge-owned request/upstream spans and metrics (see
[the security model](security.md#bounded-errors-safe-logging-and-configuration-diagnostics)
for what those artifacts do and do not contain). Correlate on the request's
`X-Correlation-ID` across logs, traces, and any downstream/upstream system
that also received it, rather than on caller-identifying detail the bridge
does not record.

If the incident implicates a specific credential (a leaked client secret,
compromised certificate, or overly broad rate limit), the safe first
response is rotation (above) or a tightened per-endpoint rate limit (see
[configuration](configuration.md)), both of which take effect on the next
restart without requiring code changes. If the incident implicates the
bridge process itself, the safe first response is to stop routing traffic to
the affected instance and redeploy from the last known-good image; because
the bridge holds no state, a fresh instance is immediately equivalent to a
healthy one once its configuration is confirmed correct.

## Safe support bundles

When escalating an issue to a maintainer or vendor, collect only: the
structured JSON logs for the affected time window, the specific
`X-Correlation-ID` values involved, and the output of `/health/ready`. Do not
collect raw configuration files, environment variable dumps, or process
memory — all three can contain a client secret, certificate password, or
private key that the bridge deliberately keeps out of every diagnostic
surface it controls. If a configuration value must be shared to diagnose a
startup failure, share only the failing key name from the
`BridgeConfigurationException` message (which never includes the invalid
value) rather than the configuration source itself.

## Rollback

Because the bridge resolves its entire configuration once at startup into
immutable options and persists nothing between requests, rollback is always
"redeploy the previous image or binary with the previous configuration" —
there is no data migration, cache, or stored state to reconcile. Verify a
rollback the same way as any deployment: `/health/ready` returns healthy, and
a synthetic request against each of `/register`, `/authorize`, and `/token`
produces the expected bounded response before routing live traffic to the
rolled-back instance.
