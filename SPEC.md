# SPEC — MCP OAuth DCR Bridge

> The single source of truth for this project. Code conforms to this document.
> Any change to architecture, components, contracts, or behavior is reflected
> here **first**, then committed, then implemented.

## 1. Purpose and structure

MCP OAuth DCR Bridge is a provider-neutral, deployable compatibility facade for
placing one remote OAuth-protected Model Context Protocol (MCP) server behind an
MCP client that requires OAuth Dynamic Client Registration (DCR).

Each deployment represents exactly one upstream MCP server and one
pre-registered upstream OAuth client. The bridge presents an OAuth 2.1-compatible
client-facing surface with DCR and S256 PKCE, while forwarding the authorization
code, token, refresh-token, and MCP bearer-token flows to the original OAuth 2.0
authorization server and MCP resource server with the smallest explicitly
configured set of changes.

The bridge is a protocol adapter, not an identity provider or OAuth broker. It
does not authenticate users, display consent, mint tokens, validate upstream
access tokens, persist tokens, refresh tokens independently, or create a second
user session. The downstream MCP client remains the token custodian. The
upstream authorization server remains the token issuer, and the upstream MCP
server remains authoritative for access-token validation and authorization.

The application is generic. No provider, product, tenant, environment, company,
or upstream-specific behavior is compiled into it. Deployment-specific routing
values are supplied only through configuration, including optional static HTTP
headers for the upstream MCP server.

### Repository structure

```text
McpOAuthDcrBridge.sln
src/
  McpOAuthDcrBridge/
tests/
  McpOAuthDcrBridge.UnitTests/
  McpOAuthDcrBridge.IntegrationTests/
  McpOAuthDcrBridge.ContractTests/
docs/
```

- `src/McpOAuthDcrBridge` is the single deployable ASP.NET Core application.
- Unit tests cover isolated validation and transformation rules.
- Integration tests run the application against in-process fake OAuth and MCP
  servers.
- Contract tests prove the externally visible OAuth, DCR, metadata, HTTP proxy,
  and streaming behavior.
- `docs` contains the threat model, protocol flows, deployment and operations
  guidance, and configuration examples.

### In scope

- OAuth protected-resource and authorization-server metadata required by MCP
  clients.
- RFC 7591-compatible DCR for a configured, pre-registered upstream client.
- Authorization-code and refresh-token grant forwarding.
- Mandatory S256 PKCE on the downstream authorization-code flow.
- Optional upstream client authentication using no credential, a client secret,
  or a certificate-backed `private_key_jwt` assertion.
- Transparent proxying of remote Streamable HTTP MCP traffic.
- Configurable optional static headers applied only to upstream MCP requests.
- Production diagnostics, health checks, container packaging, tests, and
  operational documentation.

### Out of scope

- Multiple upstream MCP servers in one process or deployment.
- User login, consent UI, identity federation, access-policy evaluation, or
  wrapper-issued access/refresh tokens.
- Access-token introspection or validation by the bridge.
- Token, authorization-code, PKCE-verifier, client-registration, or user-session
  persistence.
- Scope addition, removal, substitution, normalization, or translation.
- General query/body transformation or provider-specific compatibility logic.
- Automatic retries of OAuth requests or MCP tool calls.
- Stdio MCP transport or MCP aggregation.
- An authorization-response callback relay. Direct upstream authorization-server
  callbacks are the specified initial architecture. If interoperability testing
  proves that an upstream RFC 9207 `iss` response parameter conflicts with the
  bridge issuer, callback-relay support requires the SPEC-change protocol before
  implementation.

## 2. Components

All production components are modules in the single deployable application.
They have narrow responsibilities and share validation and forwarding primitives
rather than duplicating protocol logic.

| Component | Responsibility | Public surface |
|-----------|----------------|----------------|
| Host and composition root | Configure ASP.NET Core, dependency injection, route ordering, options validation, health checks, and graceful shutdown. | Process entry point; `/health/live`; `/health/ready` |
| Configuration model | Bind and validate the complete immutable deployment contract at startup. Reject unsafe URLs, redirect URIs, header names, credential combinations, limits, and missing values. | Configuration schema and startup validation errors |
| Protected-resource metadata | Describe the externally visible MCP resource and identify the bridge as its authorization server. | `/.well-known/oauth-protected-resource` |
| Authorization-server metadata | Advertise the bridge issuer, DCR, authorization, token, grant, public-client, and S256 capabilities. | `/.well-known/oauth-authorization-server` |
| DCR endpoint | Validate downstream client metadata and return the configured fixed client ID without creating a stored registration or issuing a client secret. | `POST /register` |
| Authorization endpoint | Validate the fixed client ID, exact redirect URI, authorization-code response type, configured scope policy, and S256 challenge, then redirect to the fixed upstream authorization endpoint while preserving accepted parameters and values. | `GET /authorize` |
| Token endpoint | Validate the downstream public-client request, forward authorization-code or refresh-token forms to the fixed upstream token endpoint, add only the configured upstream client authentication, and relay the upstream response. | `POST /token` |
| MCP challenge middleware | Return bridge discovery metadata when bearer authorization is absent and rewrite an upstream bearer challenge so clients remain bound to the bridge resource and issuer. | `401` responses on the configured MCP path |
| MCP reverse proxy | Stream MCP requests and responses between the downstream client and the fixed upstream resource server using YARP. Preserve bearer tokens, MCP session headers, methods, bodies, statuses, and streaming semantics. | `/mcp` using `GET`, `POST`, and `DELETE` as required by Streamable HTTP |
| Upstream header policy | Apply optional configured static headers to upstream MCP requests only. Configured values replace same-named downstream values. Reject forbidden transport/security headers at startup. | Configuration-driven behavior; no separate endpoint |
| Upstream client authenticator | Apply the configured token-endpoint authentication method: `none`, `client_secret_post`, `client_secret_basic`, or certificate-backed `private_key_jwt`. | Internal token-forwarding contract |
| Telemetry and redaction | Produce safe structured logs, traces, metrics, correlation, and diagnostics without exposing OAuth or MCP credentials. | Standard output and optional OTLP export |

No component may introduce a provider-specific branch. Differences between
deployments are represented by validated configuration only when they are part
of the generic contracts in this specification.

## 3. Dependencies

The implementation targets stable, supported dependencies and pins exact
versions through central package management. Dependency versions may be updated
without changing this specification when contracts and behavior remain
unchanged; behavioral or architectural changes require the SPEC-change protocol.

| Dependency | Intended version | Purpose |
|------------|------------------|---------|
| .NET SDK and runtime | .NET 10 LTS | Supported runtime, build, hosting, cryptography, HTTP, configuration, rate limiting, and health-check foundation |
| ASP.NET Core | Shared framework from .NET 10 | Minimal API endpoints, middleware, configuration, dependency injection, limits, and hosting |
| `Yarp.ReverseProxy` | 2.3.x stable | Maintained, streaming-capable HTTP reverse-proxy data plane for MCP |
| OpenTelemetry .NET packages | Stable versions compatible with .NET 10 | Vendor-neutral traces and metrics with optional OTLP export |
| xUnit | Current stable version selected at scaffolding | Automated unit, integration, and contract tests |
| `Microsoft.AspNetCore.Mvc.Testing` and ASP.NET Core TestHost | .NET 10-compatible stable versions | In-process application hosting and HTTP contract testing |
| `Microsoft.NET.Test.Sdk` and coverage collector | Current stable versions selected at scaffolding | Test execution and coverage evidence |
| Docker/OCI tooling | Current supported Dockerfile syntax | Reproducible deployment artifact and local integration testing |

The application uses `System.Text.Json`, `HttpClient`/`HttpMessageInvoker`,
`Microsoft.Extensions.Logging`, options validation, data-rate and request-size
limits, and cryptographic APIs from the .NET shared framework. It does not add a
second structured-logging framework, OAuth authorization-server product, token
store, database, cache, or provider SDK.

## 4. Telemetry

Telemetry is safe by default and useful in a stateless horizontally scaled
deployment.

### Logging

- Use `Microsoft.Extensions.Logging` structured logging to standard output.
- Production output is machine-readable JSON; development may use a concise
  console formatter.
- Accept a valid inbound correlation ID or generate one, return it to the
  caller, and propagate it upstream using a documented non-secret header.
- Record endpoint category, method, normalized route, result category, status,
  elapsed time, upstream host identifier, proxy error category, and correlation
  ID.
- Never log authorization headers, cookies, client credentials, certificate
  material, authorization codes, access tokens, refresh tokens, PKCE challenges
  or verifiers, OAuth request/response bodies, complete authorization query
  strings, configured header values, or unredacted upstream error bodies.
- Treat configured header values as secrets even when their names are not
  conventionally sensitive.

### Tracing

- Instrument inbound ASP.NET Core requests, outbound OAuth requests, and
  outbound MCP proxy requests with OpenTelemetry.
- Preserve W3C Trace Context when safe, while preventing caller-controlled trace
  data from becoming authorization input.
- Spans include route and result metadata but none of the redacted values listed
  above.
- OTLP export is optional and disabled when no endpoint is configured.

### Metrics

At minimum emit:

- Request count, status class, and duration by endpoint category.
- Upstream OAuth and MCP request count, status class, and duration.
- DCR and OAuth validation rejection counts by bounded reason code.
- Proxy transport failures and timeouts by bounded category.
- Current active MCP requests/streams.
- Process/runtime metrics supported by the selected OpenTelemetry packages.

Metric labels must be bounded. Client IDs, redirect URIs, scopes, tenant/user
identifiers, configured header names/values, authorization codes, MCP tool names,
and arbitrary upstream error values are forbidden as labels.

### Health

- `/health/live` reports process liveness without making outbound requests.
- `/health/ready` reports successful startup configuration validation and local
  readiness. It does not call the authorization or resource server on every
  probe.
- Optional active dependency diagnostics, if added later, must use a separate
  protected operational endpoint and must not change readiness semantics without
  a specification change.

## 5. System interactions

### Fixed trust boundaries

The deployment configuration defines all outbound origins. No inbound request
may select or override an authorization endpoint, token endpoint, or MCP
upstream. Startup validation requires absolute HTTPS URLs outside explicitly
enabled local-development settings. Redirect URIs are exact configured values,
not host patterns or caller-supplied registrations.

### Client-facing discovery

1. An unauthenticated request to `/mcp` receives `401 Unauthorized` and a
   `WWW-Authenticate: Bearer` challenge whose `resource_metadata` points to the
   bridge protected-resource metadata.
2. Protected-resource metadata identifies the externally configured `/mcp`
   resource, the bridge issuer, bearer header usage, and configured upstream
   scopes.
3. Authorization-server metadata identifies the externally configured bridge
   issuer and the bridge `/register`, `/authorize`, and `/token` endpoints. It
   advertises `authorization_code` and `refresh_token`, response type `code`,
   downstream token authentication method `none`, and PKCE method `S256`.

The external issuer and resource URLs are derived from one required canonical
external base URL plus fixed application paths. Forwarded host headers cannot
change published metadata.

Discovery metadata requests are bodyless: a metadata `GET` with a nonzero
declared content length or transfer-encoded body is rejected with a bounded
`400 Bad Request` response without buffering, parsing, or logging the body.
This constraint applies only to the two discovery metadata paths; it does not
apply to the Streamable HTTP MCP path.

### Dynamic client registration

1. The downstream client sends RFC 7591 client metadata to `/register`.
2. The bridge accepts only redirect URIs that exactly match configured allowed
   values. Every supplied URI must be allowed.
3. Requested response types, grant types, and token authentication must be
   compatible with authorization code, refresh token, response type `code`, and
   public-client method `none`.
4. If a registration scope is supplied, it is validated by the configured scope
   policy and preserved in the response; it is never translated.
5. The bridge returns the configured fixed client ID, no client secret,
   `token_endpoint_auth_method: none`, and normalized supported registration
   metadata.
6. Registration is deterministic and stateless. Repeated valid registrations
   may receive the same fixed client ID. No registration access token or client
   management endpoint is issued.

### Authorization-code flow

1. `/authorize` accepts only the configured client ID, an exact allowed redirect
   URI, `response_type=code`, a nonempty `code_challenge`, and
   `code_challenge_method=S256`.
2. The bridge validates the requested scopes when an allowlist is configured.
   It does not modify the scope value or order.
3. Accepted standard and extension query parameters, including `state`, PKCE,
   scope, prompt, hints, and RFC 8707 `resource`, are forwarded without semantic
   modification. The bridge does not add, remove, map, or normalize them.
4. The authorization request uses the configured upstream client ID. The
   initial architecture uses the same configured client ID returned by DCR, so
   no client-ID translation is expected.
5. The upstream authorization server redirects directly to the configured
   downstream callback. The bridge does not receive the authorization code.

The bridge metadata issuer is the bridge canonical external URL. The upstream
authorization server remains the access-token issuer. Access tokens are opaque
to the bridge and downstream OAuth client and are validated only by the upstream
MCP resource server. The initial direct-callback design assumes the upstream
authorization response does not include an RFC 9207 `iss` value that conflicts
with the bridge issuer. This assumption is an explicit external interoperability
test gate.

### Token and refresh flow

1. `/token` accepts `application/x-www-form-urlencoded` requests no larger than
   the configured hard limit, which defaults to 16 KiB.
2. `grant_type` is limited to `authorization_code` and `refresh_token`.
3. The supplied client ID must equal the configured fixed client ID. Downstream
   client secrets, assertions, or Authorization-based client credentials are
   rejected to prevent credential smuggling.
4. Authorization-code requests require the exact allowed redirect URI and a
   nonempty `code_verifier`. The verifier is forwarded unchanged; the upstream
   authorization server performs the authoritative PKCE binding check.
5. Refresh-token requests are forwarded only under the configured fixed client
   identity. The bridge does not read, store, rotate, or reuse the refresh token.
6. Form fields, including `scope` when present, are forwarded without semantic
   modification except for the configured upstream client-authentication method.
7. Upstream client authentication is one of:
   - `none`: add no credential;
   - `client_secret_post`: add the configured secret as a form parameter;
   - `client_secret_basic`: add the configured client credentials using HTTP
     Basic authentication;
   - `private_key_jwt`: generate a short-lived, uniquely identified assertion
     for the fixed upstream client and exact token endpoint, signed by the
     configured certificate/private key, and add the standard assertion fields.
8. The upstream status code, token JSON or OAuth error body, content type, and
   safe response headers are relayed without token substitution or schema
   translation. Hop-by-hop headers are never relayed.
9. Token and refresh requests are never automatically retried.

### MCP proxy flow

1. The externally visible MCP resource is `/mcp`.
2. The configured upstream MCP URL may contain any fixed path. `/mcp` is mapped
   to that exact configured URL without exposing a general-purpose forward
   proxy.
3. Bearer authorization is forwarded unchanged. The bridge checks for presence
   to produce discovery challenges but does not validate token contents.
4. Request methods, bodies, accepted media types, Streamable HTTP responses,
   server-sent event bodies used by Streamable HTTP, and MCP session headers
   such as `Mcp-Session-Id` and `Last-Event-ID` are preserved.
5. Request and response bodies are streamed and are not buffered or interpreted
   by the bridge.
6. An upstream `401` bearer challenge is rewritten only as needed to identify
   the bridge protected-resource metadata. OAuth error and scope information is
   otherwise preserved.
7. MCP calls are never automatically retried because tool calls may have side
   effects.

### Configured upstream MCP headers

- Configuration may contain zero or more static name/value headers.
- Headers are applied only to requests sent to the upstream MCP server, never to
  authorization, token, discovery, health, or DCR requests.
- A configured value replaces any same-named downstream value. The replacement
  rule is case-insensitive and prevents downstream override.
- Header order has no semantic contract. Multiple configured values for one
  header are supported only when explicitly represented by the configuration
  model and valid for that header.
- Startup validation rejects `Authorization`, `Host`, `Content-Length`,
  `Transfer-Encoding`, `Connection`, `Upgrade`, proxy/forwarding headers,
  tracing/correlation headers controlled by the application, and MCP session or
  protocol headers. The implementation maintains one documented forbidden-header
  set and uses it consistently in validation and forwarding.
- Header values may be supplied by environment or external secret providers.
  They are always redacted from logs, traces, metrics, errors, diagnostics, and
  configuration dumps.

### Failure behavior

- Invalid downstream input fails closed with an OAuth-appropriate `400` response
  or `401` challenge and a bounded, non-sensitive error description.
- Configuration errors prevent readiness and process startup.
- Upstream OAuth errors are relayed as OAuth errors without reinterpretation.
- Proxy connection failures and timeouts become consistent `502`, `503`, or
  `504` responses as appropriate and include a correlation ID, never secrets.
- Client cancellation immediately cancels outbound work and streaming.
- Graceful shutdown stops accepting new requests and allows active requests a
  configurable bounded drain period.

---

## Commitments

These are binding requirements for the implementation, not aspirations.

### 6. Authentication & authorization

The bridge does not establish a separate user identity or authorization layer.
It enforces protocol authorization boundaries as follows:

- DCR and authorization requests are restricted to exact configured redirect
  URIs and the one fixed client ID.
- Authorization code is the only supported interactive response type.
- S256 PKCE is mandatory. `plain`, missing, or unsupported challenge methods are
  rejected.
- The downstream token endpoint is a public-client endpoint. Upstream client
  credentials exist only inside the bridge and are never returned downstream.
- Scope values are forwarded unchanged. When configured, an exact scope
  allowlist rejects unapproved scopes but never rewrites approved scopes.
- The upstream MCP server remains the sole validator and authorizer of bearer
  access tokens. The bridge forwards only tokens presented for its fixed MCP
  upstream and never sends them to discovered or caller-selected hosts.
- Static upstream header configuration cannot override bearer authorization,
  transport security, forwarding identity, tracing controls, or MCP protocol
  headers.
- Production ingress requires HTTPS. TLS may terminate at a trusted ingress, but
  forwarded-host trust is explicit and cannot alter security metadata.
- Secrets and private keys are loaded from configuration providers intended for
  secrets, never from committed configuration. Certificate/private-key access is
  least privilege.
- Built-in ASP.NET Core rate limiting protects DCR, authorization, and token
  endpoints using configurable bounded policies. Rate-limit keys must not expose
  or persist credentials.
- Outbound destinations are fixed at startup, preventing SSRF and open-proxy
  behavior.

The threat model must cover confused-deputy attacks, malicious DCR metadata,
redirect manipulation, authorization-server mix-up, credential smuggling, PKCE
downgrade, token leakage, header spoofing, request smuggling, SSRF, replay,
denial of service, unsafe logging, and cross-instance behavior.

### 7. Performance characteristics

- Non-streaming MCP requests add less than 10 ms p95 bridge processing latency
  under 100 concurrent requests on the documented reference environment,
  excluding network and upstream time.
- One instance supports at least 100 concurrent active MCP streams on the
  reference environment without response buffering or unbounded memory growth.
- OAuth and metadata endpoints support at least 100 requests per second on the
  reference environment while respecting rate limits.
- MCP request and response bodies are streamed. Memory consumption is bounded by
  configured HTTP buffers, concurrency, and framework overhead rather than body
  size.
- Token request bodies default to a 16 KiB maximum. DCR JSON bodies default to a
  32 KiB maximum. Limits are configurable only within documented safe bounds.
- OAuth outbound requests have a configurable timeout that defaults to 30
  seconds. MCP streaming uses a configurable activity timeout rather than a
  fixed total-response timeout.
- Client cancellation propagates promptly upstream. Shutdown has a configurable
  drain period with a documented default.
- Performance targets are verified by automated repeatable tests or benchmarks
  whose reference hardware and methodology are documented.

### 8. Full testing coverage

The system must be **thoroughly tested**. No untested production behavior may
advance to the next milestone.

Required coverage includes:

- Unit tests for every configuration validator, redirect/client/scope rule,
  DCR metadata rule, PKCE rule, forbidden-header rule, credential mode, error
  mapping, metadata document, and redaction rule.
- Integration tests with in-process fake authorization and MCP servers for every
  success and failure path, without internet or production credentials.
- Contract tests proving exact semantic forwarding of scopes, extension
  parameters, state, PKCE challenge/verifier, code, redirect URI, refresh token,
  safe headers, status codes, OAuth errors, and token responses.
- DCR tests proving deterministic fixed-client registration, exact callback
  enforcement, public-client behavior, request limits, and absence of storage.
- Token-client-authentication tests for `none`, `client_secret_post`,
  `client_secret_basic`, and `private_key_jwt`, including negative credential
  and assertion tests.
- MCP transport tests for `GET`, `POST`, and `DELETE`, Streamable HTTP streaming,
  cancellation, session headers, custom static headers, large bodies, upstream
  challenges, and graceful shutdown.
- Security-negative tests for open redirect, arbitrary outbound destination,
  PKCE downgrade, client-ID substitution, downstream credential smuggling,
  header override, request-size abuse, malformed form/JSON input, unsafe
  forwarded headers, and secret exposure.
- Telemetry tests proving required events exist and forbidden sensitive values
  never appear in logs, spans, metrics, errors, or health output.
- Performance tests for the targets in section 7.
- A documented manual end-to-end acceptance procedure against a real MCP client,
  authorization server, and MCP server. It explicitly verifies refresh-token
  acquisition, downstream-managed refresh, direct callback behavior, RFC 9207
  authorization-response issuer compatibility, and RFC 8707 `resource`
  pass-through.

Test doubles must model protocols, not special-case implementation internals.
Tests follow the same DRY and coding standards as production code. Flaky tests,
ignored failures, and network-dependent automated tests block advancement.

### 9. DRY

The DRY (Don't Repeat Yourself) principle is honored **absolutely**. No
duplicated logic, boilerplate, validation rules, forwarding rules, error mapping,
redaction lists, configuration keys, test setup, or query/form handling may
exist across endpoints, proxy code, or tests. Repeated behavior is abstracted
into cohesive reusable helpers, extensions, fixtures, builders, or shared
classes with one authoritative definition.

### 10. Coding standards

The highest coding standards are upheld: idiomatic, readable, consistent,
maintainable C# that follows current .NET design guidance and the `code-quality`
skill during implementation and review.

- Every type and public member is documented.
- There is exactly one top-level type definition per file, including internal
  production and test types.
- Nullable reference types, analyzers, warnings-as-errors, deterministic builds,
  formatting enforcement, and dependency vulnerability auditing are enabled.
- Async I/O is used end to end. Cancellation tokens are accepted and propagated.
- Mutable global state, service locators, hidden network calls, and sync-over-async
  are forbidden.
- Types have single responsibilities and dependencies are explicit.
- Security-sensitive comparisons, URI handling, encoding, cryptography, and
  header/form parsing use platform primitives and standards-compliant libraries.
- Comments explain intent, invariants, and non-obvious security reasoning rather
  than restating code.
- Every milestone includes a reviewer pass. Security-sensitive OAuth and proxy
  code receives focused threat-model review.

### 11. Documentation

Documentation is **mandatory** and changes in the same milestone as behavior.

- XML documentation comments are required on every public type and public
  member. Complex internal protocol/security types also require XML comments.
- `README.md` documents purpose, non-goals, quick start, local development,
  testing, container execution, and the explicit transparent-token model.
- `docs/architecture.md` documents components, trust boundaries, and sequence
  diagrams for discovery, DCR, authorization, code exchange, refresh, and MCP
  proxying.
- `docs/security.md` contains the threat model, credential handling, redirect
  and PKCE invariants, issuer model, header restrictions, redaction contract,
  and security reporting process.
- `docs/configuration.md` is the authoritative configuration reference,
  including all defaults, validation, secret sources, client-authentication
  modes, exact redirect URIs, scopes, custom headers, limits, and examples using
  fictional providers only.
- `docs/deployment.md` covers OCI deployment, TLS/ingress assumptions, scaling,
  health probes, graceful shutdown, and secret/certificate mounting.
- `docs/operations.md` covers telemetry, alerts, failure diagnosis, credential
  rotation, incident response, and safe support-data collection.
- `docs/testing.md` documents automated suites, performance methodology, and the
  manual external interoperability acceptance procedure.
- OpenAPI or equivalent machine-readable endpoint documentation is generated or
  maintained for the bridge-owned metadata, DCR, authorization, token, and
  health endpoints without exposing secrets.

Documentation and examples must remain provider-neutral. They may use fictional
domains, scopes, headers, and identifiers, but no real upstream product is a
named dependency or special case.

---

## Change log

| Date | Change | Reason / logic shift |
|------|--------|----------------------|
| 2026-07-22 | Initial specification | Establish a provider-neutral, stateless OAuth DCR compatibility facade with transparent token handling, YARP MCP proxying, and optional configured upstream MCP headers. |
