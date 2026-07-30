# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M8 — YARP MCP reverse proxy and configured headers

### M8-1 (required-test gap): no absolute-form request-target SSRF test

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/McpProxyContractTests.cs`.

**Problem.** M8's required tests include "Open-proxy/SSRF tests using
**absolute-form targets**, Host/forwarding headers, path traversal, encoded
paths, redirects, and malicious upstream responses". Host/forwarding poisoning,
traversal, encoded paths, unfollowed redirects, and the challenge rewrite are
all covered — but nothing sends an absolute-form request target
(`GET http://attacker.example.test/mcp HTTP/1.1` on the wire), which is the
classic probe for open-proxy behavior. `HttpClient` with a `BaseAddress`
cannot produce this form, which is presumably why it was skipped; it needs a
raw socket.

**Guidance.**

1. Add a contract test that opens a raw `TcpClient` to the bridge, writes an
   absolute-form request line targeting a foreign authority with the `/mcp`
   path (e.g. `GET http://attacker.example.test/mcp HTTP/1.1`, a valid `Host`
   header, an `Authorization: Bearer …` header, and `Connection: close`), and
   reads the raw response.
2. Assert the bridge either rejects the request outright (`400`) or — if
   Kestrel normalizes it onto the local route — proxies it only to the fixed
   configured upstream: `fakeUpstream.RequestCount` documents which, and no
   connection is ever attempted to the foreign authority (use an unresolvable
   or sentinel host so any attempt would fail loudly).
3. Result that demonstrates resolution: the suite pins that absolute-form
   targets cannot select a proxy destination, closing the last untested cell
   of the required SSRF matrix.

### M8-2 (required-test gap): the "no-buffer" half of the no-buffer/no-retry tests is missing

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/McpProxyContractTests.cs`,
`tests/McpOAuthDcrBridge.ContractTests/FakeUpstreamMcpServer.cs`.

**Problem.** M8 requires "**No-buffer**/no-retry tests". No-retry is proven
(500 and unreachable-upstream cases assert `RequestCount == 1`), but nothing
proves the bridge does not buffer response bodies. The 512 KiB large-body test
only checks byte-for-byte equality after completion — a fully buffering proxy
would pass it. The acceptance criterion "YARP does not buffer or interpret MCP
bodies" currently has no evidence. (M9 will add deeper incremental-delivery
and SSE tests; M8 still owes the basic proof for the transport it shipped.)

**Guidance.**

1. Extend `FakeUpstreamMcpServer` so a scripted handler can write and flush a
   first chunk, then await a `TaskCompletionSource` before writing the rest
   (expose the TCS to the test).
2. Add a contract test that sends a proxied `GET /mcp`, reads the response as
   a stream (`HttpCompletionOption.ResponseHeadersRead`), asserts the first
   chunk arrives while the upstream handler is still blocked on the TCS, then
   releases the TCS and asserts the remainder arrives and the stream ends.
3. Result that demonstrates resolution: a change that introduces whole-body
   buffering in the proxy path deadlocks or times out this test.

### M8-3 (minor test-strength gap): no test applies multiple distinct configured headers together

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/McpProxyContractTests.cs`.

**Problem.** M8 requires "Static-header tests for **zero/multiple headers**,
replacement, casing, multiple configured values, downstream spoofing, and
isolation". Zero headers, one header (with replacement and casing), and one
header with multiple values are covered, but no test configures two or more
*distinct* headers on the same deployment and proves all are applied in one
forwarded request. A regression that applied only the first configured entry
would pass today's suite.

**Guidance.**

1. Extend one static-header test (or add one) configuring at least two
   distinct headers (e.g. `X-Deployment-Context` and `X-Ambient-Region`), one
   of which also replaces a downstream value, and assert both arrive upstream
   with their configured values in a single request.
2. Result that demonstrates resolution: applying fewer than all configured
   headers fails the contract suite.

### M8-4 (DRY violation): the bridge challenge value is constructed independently in two places

**Status:** open

**Where.**
- `src/McpOAuthDcrBridge/Mcp/McpChallengeMiddleware.cs` (`WriteChallenge`)
- `src/McpOAuthDcrBridge/Mcp/McpProxyConfiguration.cs` (`RewriteBearerChallenge`)

**Problem.** Both sites independently build
`new Uri(options.IssuerUri, ".well-known/oauth-protected-resource").AbsoluteUri`
and the `resource_metadata="…"` challenge parameter. This is the
security-critical value that binds MCP clients to the bridge; if the two
copies drift (for example one later switches to a different well-known path or
quoting), the local challenge and the rewritten upstream challenge would point
clients at different metadata. SPEC §9 requires one authoritative definition,
and `BridgeOptions` already owns the canonical public-URI family
(`McpResourceUri`, `RegistrationUri`, …) that this value belongs to.

**Guidance.**

1. Add a documented `ProtectedResourceMetadataUri` property to `BridgeOptions`
   beside the existing `PublicUri`-derived properties.
2. Add one shared helper (e.g. a `BearerChallenge` type in
   `src/McpOAuthDcrBridge/Mcp/`) that produces the challenge parameter/header
   value from that URI plus optional preserved parameters, and use it from
   both the middleware and the response transform.
3. Result that demonstrates resolution: the metadata URI and the
   `resource_metadata` parameter format each have exactly one definition, and
   both existing challenge contract tests still pass unchanged.

### M8-5 (minor edge case): quoted challenge parameters containing commas are mangled

**Status:** open

**Where.** `src/McpOAuthDcrBridge/Mcp/BearerChallengeParameters.cs`,
`tests/McpOAuthDcrBridge.UnitTests/Mcp/BearerChallengeParametersTests.cs`.

**Problem.** `Parse` splits the auth-param list on every comma before looking
at quotes, so a legitimate upstream value like
`error_description="code expired, retry later"` is truncated to
`"code expired` (with a dangling fragment discarded or misparsed). M8's
acceptance criteria require that "safe OAuth error and scope information" is
**preserved** in the rewritten challenge; RFC 7235 quoted-strings may contain
commas, and error descriptions realistically do.

**Guidance.**

1. Make `Parse` quote-aware: walk the string once, splitting on commas only
   outside double quotes, and unescape `\"` inside quoted values (a small
   state loop; no regex needed).
2. Add unit cases: a quoted value containing a comma, a quoted value
   containing an escaped quote, and a trailing unterminated quote (must parse
   as best-effort or drop that pair, never throw).
3. Result that demonstrates resolution: the rewritten upstream challenge
   carries the full `error_description` through the contract test when the
   fake upstream sends a comma-containing description.
