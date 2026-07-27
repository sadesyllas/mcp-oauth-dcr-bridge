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
