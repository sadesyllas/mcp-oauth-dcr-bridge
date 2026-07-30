# MCP reverse proxy

`GET`, `POST`, and `DELETE` requests to `/mcp` are streamed to the one
configured upstream MCP server using YARP. The bridge never validates, parses,
stores, or logs the bearer token; it only checks for its presence before
deciding whether to proxy at all.

## Routing and destination safety

YARP is configured programmatically, not from a config file, with exactly one
route and one cluster derived from `Bridge:Upstream:McpUrl`. The route matches
only the literal path `/mcp`; the outbound request always targets the upstream
origin (scheme, host, and port) with its path replaced by the configured
upstream path, regardless of any request input. No `Host` header, forwarding
header, absolute-form request target, encoded path, or path-traversal attempt
can change the scheme, host, port, or path that is contacted — there is no
general-purpose forward-proxy behavior, only this one fixed mapping. A request
whose path merely resembles `/mcp` (an extra segment, an encoded separator, a
traversal sequence) never matches the route and never reaches the proxy.

## Challenge and credential handling

A request without a bearer credential is short-circuited by
`McpChallengeMiddleware` before routing: it returns the bridge's own
`401` challenge (identifying `/.well-known/oauth-protected-resource`) and never
forwards anything upstream. A request that does carry a bearer credential is
proxied unchanged; the bridge does not inspect the token.

If the upstream itself returns `401`, a response transform rewrites its
`WWW-Authenticate` header so the `resource_metadata` parameter identifies the
bridge's own protected-resource metadata instead of the upstream's. Any
`error`, `error_description`, and `scope` parameters on the upstream challenge
are preserved verbatim; every other parameter (for example a `realm` that would
identify the upstream) is dropped.

## Request and response fidelity

Methods, bodies, status codes, content types, and MCP protocol/session headers
(`Mcp-Session-Id`, `Last-Event-ID`, `MCP-Protocol-Version`) are preserved in
both directions. Hop-by-hop headers (`Connection`, `Keep-Alive`,
`Transfer-Encoding`, and similar) are never relayed either direction, per
standard HTTP proxy rules. Bodies are streamed, not buffered or interpreted.
YARP does not automatically retry a failed or redirected upstream response; a
`3xx` from the upstream is relayed to the caller unchanged rather than followed
by the bridge, and a connection failure maps to a bounded `502`/`503`/`504`
without a second attempt.

## Streaming, sessions, and lifecycle

The proxy has no fixed total-duration timeout on `/mcp`: a Streamable HTTP
response or server-sent-event stream may run indefinitely as long as it keeps
making progress. Instead, `Bridge:Limits:McpActivityTimeoutSeconds` bounds
inactivity — the timer resets on every byte moved in either direction, so an
actively streaming response is never cut off purely because of its total
elapsed time, while a stream that goes silent for longer than the configured
window is ended. `Mcp-Session-Id`, `Last-Event-ID`, and `Accept` are forwarded
on every request exactly as received, so a client's reconnect — a fresh
request carrying the session and last-event IDs it was given — resumes the
same logical session from the upstream's perspective; the bridge holds no
session state of its own.

Client disconnection or cancellation of an in-progress request immediately
cancels the outbound call to the upstream, releasing the connection rather
than continuing to consume upstream resources for an abandoned request. If the
upstream disconnects abruptly mid-response, the already-sent status code and
headers are never replaced — the client sees a truncated body rather than a
retroactive error page — and the bridge does not retry, since a partially
delivered MCP response may already have had side effects upstream. One
instance supports at least 100 concurrent active `/mcp` streams without
cross-session leakage, since each request's outbound connection, headers, and
body are independent.

Graceful shutdown is bounded by `Bridge:Limits:ShutdownDrainTimeoutSeconds`: new
requests stop being accepted, and in-flight requests (including open streams)
are given up to that window to complete before being forcibly ended.

## Configured static headers

Optional `Bridge:Upstream:McpHeaders` entries are added to the outbound
request only, replacing any same-named downstream header case-insensitively. A
downstream value can never survive under a configured name. These headers are
never applied to OAuth, discovery, DCR, token, or health requests, since only
`/mcp` is routed through the proxy. The same forbidden-header set enforced at
startup (`Authorization`, `Host`, hop-by-hop, forwarding, tracing/correlation,
and MCP session/protocol headers) is checked again at forwarding time.

See [the security model](security.md) for the SSRF/open-proxy threat model and
[configuration](configuration.md) for the static-header schema.
