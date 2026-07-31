# Testing

## Automated suites

| Suite | What it proves | How it runs |
|---|---|---|
| `McpOAuthDcrBridge.UnitTests` | Isolated validation, transformation, and parsing-boundary rules (configuration bounds, scope policy, header validators, deterministic fuzz tests, OpenAPI document consistency) without a hosted application. | `dotnet test tests/McpOAuthDcrBridge.UnitTests` |
| `McpOAuthDcrBridge.IntegrationTests` | The application host end to end — startup, telemetry canary sweeps, shared configuration contracts — against fake upstreams, in-process. | `dotnet test tests/McpOAuthDcrBridge.IntegrationTests` |
| `McpOAuthDcrBridge.ContractTests` | Every externally visible HTTP/protocol contract: discovery, DCR, authorization, token, MCP proxying, streaming, security headers, rate limiting, and the performance benchmarks below — against real Kestrel over loopback HTTP, with fake upstream OAuth/MCP servers. | `dotnet test tests/McpOAuthDcrBridge.ContractTests` |

Run everything with one command from the repository root, matching CI:

```sh
dotnet restore McpOAuthDcrBridge.sln
dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore
dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore
dotnet test McpOAuthDcrBridge.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet list McpOAuthDcrBridge.sln package --vulnerable --include-transitive
trivy image --severity HIGH,CRITICAL --exit-code 1 --ignore-unfixed mcp-oauth-dcr-bridge:local
```

`dotnet restore` also runs NuGet's built-in vulnerability audit (see
[the dependency management section of the security model](security.md#dependency-and-container-vulnerability-management));
an unresolved high or critical advisory fails the restore, and therefore the
build, before any test even runs. The image scan is the same gate applied to
the OS/runtime layers the NuGet audit cannot see — see
[image vulnerability scanning](deployment.md#image-vulnerability-scanning)
for the alternative `docker scout` command and when to re-run it.
`scripts/container-smoke-test.sh` runs this scan automatically as its last
step against the image it builds.

## Performance methodology and reference results

SPEC.md §7 sets three repeatable targets, each proven by a dedicated test in
`tests/McpOAuthDcrBridge.ContractTests/Performance/PerformanceBenchmarkTests.cs`
rather than a one-off manual measurement:

- **Non-streaming p95 bridge processing latency under 100 concurrent
  requests, excluding network/upstream time** — measured by listening
  directly to the bridge's own `bridge.request.duration` OpenTelemetry
  histogram (the same metric documented in
  [operations](operations.md)) for 100 concurrent `POST /register` calls
  (a route that makes no upstream call, isolating bridge-owned processing
  time from any network hop), after a warm-up batch.
- **At least 100 requests/second on OAuth/metadata endpoints while rate
  limits behave as documented** — measured by issuing 500 concurrent
  `GET /.well-known/oauth-authorization-server` requests through the real
  rate-limiting middleware (configured with a permit limit far above the
  measured load, so the limiter is present and active but not the
  bottleneck) and dividing by wall-clock elapsed time.
- **At least 100 concurrent active MCP streams without unbounded memory
  growth** — measured by opening 120 concurrent `/mcp` streams against a
  fake upstream that holds each one open, snapshotting the managed heap
  (`GC.GetTotalMemory` after a forced full collection) before, during, and
  after the streams close.

Each test asserts a generous regression-guard threshold — not the tight SPEC
number — because the suite runs on whatever machine invokes it and a tight
absolute-time assertion would be flaky on shared or loaded hardware. The
actual numbers below are the reference measurement, captured on this
documented environment; re-run
`dotnet test tests/McpOAuthDcrBridge.ContractTests --filter FullyQualifiedName~PerformanceBenchmarkTests --logger "console;verbosity=detailed"`
to reproduce them.

**Reference environment:** Dell Precision 5690, Intel Core Ultra 7 165H
(16 cores / 22 threads), 64 GiB RAM, Windows 11 Enterprise 10.0.26200,
.NET SDK 10.0.302, Release configuration, loopback HTTP (no real network
hop). This is a developer workstation, not a dedicated performance
reference server; absolute numbers on production-grade server hardware
would be expected to differ, likely favorably.

**Results across three consecutive runs** (2026-07-31):

| Target | SPEC threshold | Measured |
|---|---|---|
| Non-streaming p95 processing latency, 100 concurrent `/register` requests | < 10 ms | 0.64 – 1.25 ms |
| OAuth/metadata sustained throughput, 500 concurrent discovery requests | ≥ 100 req/s | 3,047 – 3,617 req/s |
| Managed heap growth, 120 concurrent `/mcp` streams held open | bounded, no unbounded growth | 10.2 – 15.0 MiB active; 7.4 – 13.1 MiB residual after close |

All three targets are met with substantial margin. The 120-stream heap
growth figure is managed-heap only (not total process working set) and
reflects buffering already bounded by configured HTTP limits and framework
overhead rather than MCP body size, consistent with the streaming design in
[the MCP reverse proxy](mcp-proxy.md).

## Manual external interoperability acceptance procedure

The automated suites above use fake, in-process OAuth and MCP servers by
design (see [SPEC.md](../SPEC.md)'s architecture: unit/integration/contract
tests never depend on a real external provider). SPEC.md §7 additionally
requires acceptance evidence against **real** external systems: a real MCP
client, a real OAuth authorization server, and a real MCP server. This
checklist is that manual procedure. It has not been executed as part of this
milestone — no such live systems are available in this environment — and is
provided so an operator with real systems can run it and record evidence.
Any assumption below that fails triggers the SPEC-change protocol in
`SPEC.md`; it must not be silently patched around.

Before starting: configure a real deployment (see
[deployment](deployment.md)) pointing at the real authorization server and
MCP server's actual endpoints, using only test/sandbox credentials — never
production user data or production credentials in any captured evidence.

1. **Discovery.** Point the real MCP client at the bridge's `/mcp` origin.
   Confirm it discovers OAuth via the `401` challenge and fetches
   `/.well-known/oauth-protected-resource` and
   `/.well-known/oauth-authorization-server` successfully.
2. **DCR.** Confirm the client's dynamic registration against `/register`
   succeeds and the client accepts the fixed `token_endpoint_auth_method:
   none` public-client response without requiring a secret.
3. **Authorization and S256 PKCE.** Confirm the client generates and sends a
   S256 `code_challenge`, is redirected through the bridge to the real
   authorization server's login/consent screen, and is redirected by the
   *authorization server* — not the bridge — directly to the client's own
   `redirect_uri` with the authorization code (the direct-callback model
   documented in [authorization](authorization.md)).
4. **Unchanged scopes.** If the client requests a scope, confirm the scope
   presented to the user for consent, and the scope in the final token
   response, match what the client requested unchanged.
5. **Original token response.** Confirm the token response the client
   receives from `/token` is byte-for-byte the real authorization server's
   response (same field set, same token format), proving the bridge relayed
   rather than reinterpreted it.
6. **Downstream refresh.** Exhaust or wait out the access token's lifetime,
   confirm the client's refresh via `/token` with `grant_type=refresh_token`
   succeeds against the real authorization server, and that any rotated
   refresh token is relayed back to the client unchanged.
7. **Static MCP headers.** If the deployment configures
   `Bridge:Upstream:McpHeaders`, confirm the real MCP server observes exactly
   those header values on every forwarded request.
8. **RFC 9207 issuer compatibility.** Confirm the real authorization
   server's redirect response does not carry an `iss` parameter that
   conflicts with the bridge's own issuer identity (the direct-callback
   assumption documented in [the security model](security.md)); if it does,
   this is a failed assumption requiring the SPEC-change protocol, not a
   workaround.
9. **RFC 8707 `resource`.** If the client sends a `resource` parameter,
   confirm it is forwarded unchanged to the authorization server and, if
   echoed, unchanged back to the client.
10. **Streamable HTTP.** Confirm a real MCP tool call over `/mcp` streams
    incrementally (the client receives partial output before the call
    finishes) and that `Mcp-Session-Id` survives a reconnect.

Record evidence as redacted request/response logs or a written pass/fail
per step, with every token, secret, and personally identifying value
removed before it enters any repository artifact — see
[safe support bundles](operations.md#safe-support-bundles) for what is and
is not safe to capture.
