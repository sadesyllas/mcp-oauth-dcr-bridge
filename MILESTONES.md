# MILESTONES — MCP OAuth DCR Bridge

> Derived from `SPEC.md`. Implemented strictly in order, one after another.
> A milestone may begin only after the previous one is **Done** and **Tested**.
> Status legend: ✅ achieved · ⬜ not yet.

## Tracking

| # | Milestone | Done | Tested | Reviewed |
|---|-----------|:----:|:------:|:--------:|
| M0 | Solution scaffold and engineering gates | ✅ | ✅ | ✅ |
| M1 | Immutable configuration and trust boundaries | ✅ | ✅ | ✅ |
| M2 | Safe telemetry, correlation, and health | ✅ | ✅ | ✅ |
| M3 | OAuth discovery metadata and MCP challenge | ✅ | ✅ | ✅ |
| M4 | Stateless fixed-client DCR | ✅ | ✅ | ✅ |
| M5 | Transparent authorization forwarding and S256 PKCE | ⬜ | ⬜ | ⬜ |
| M6 | Transparent token and refresh forwarding | ⬜ | ⬜ | ⬜ |
| M7 | Certificate-backed private-key JWT authentication | ⬜ | ⬜ | ⬜ |
| M8 | YARP MCP reverse proxy and configured headers | ⬜ | ⬜ | ⬜ |
| M9 | Streaming, session, cancellation, and failure semantics | ⬜ | ⬜ | ⬜ |
| M10 | Security and operational hardening | ⬜ | ⬜ | ⬜ |
| M11 | Packaging, performance, and release readiness | ⬜ | ⬜ | ⬜ |

- **Done** — implemented.
- **Tested** — thorough tests written **and** passing.
- **Reviewed** — a reviewer has gone through the changes and they passed review.

Advancing to the next milestone requires the current one to be **Done + Tested**.
**Reviewed** is filled by the reviewer pass defined by the `spec-exec` skill and
may legitimately lag behind implementation.

Every milestone must update the documentation affected by its behavior. All
production and test code follows the coding, documentation, DRY, and one-top-level-
type-per-file commitments in `SPEC.md`.

---

## M0 — Solution scaffold and engineering gates

**Scope.** Establish the buildable .NET 10 solution, project boundaries, central
package/version management, analyzers, formatting, warnings-as-errors, test
infrastructure, deterministic builds, coverage collection, and minimal ASP.NET
Core host. Add no OAuth or proxy behavior.

**Acceptance criteria.**

- The repository structure exactly matches `SPEC.md`.
- The solution builds from a clean checkout using the documented .NET 10 SDK.
- Production, unit, integration, and contract-test projects are present with
  correct dependency direction; production code never references test code.
- Nullable reference types, warnings-as-errors, .NET analyzers, deterministic
  builds, formatting enforcement, and package vulnerability auditing are enabled.
- Central package management pins every external dependency.
- The application starts and stops cleanly but exposes no product endpoints yet.
- Repository-wide build, format-check, and test commands are documented.
- A baseline `README.md` explains purpose, non-goals, transparent-token model,
  repository structure, and development commands without provider-specific
  examples.

**Required tests.**

- Clean restore and release build.
- Formatting/analyzer gate with zero warnings.
- Empty automated test suites execute successfully through one repository-level
  command.
- Host smoke test proves startup and graceful process cancellation.
- Dependency vulnerability audit produces no known high or critical findings.

---

## M1 — Immutable configuration and trust boundaries

**Scope.** Implement the complete typed configuration contract and fail-fast
startup validation for canonical external URLs, upstream OAuth and MCP URLs,
fixed client identity, exact redirect URIs, scopes, credential modes, custom
MCP headers, rate/size/time limits, trusted forwarding, and environment/secret
overrides. Establish fixed outbound destinations before adding network behavior.

**Acceptance criteria.**

- All options are immutable after successful startup validation.
- The canonical external base URL deterministically produces the bridge issuer,
  MCP resource, and public endpoint URLs; inbound forwarded hosts cannot alter
  them.
- Production URLs require HTTPS; explicitly documented local-development
  exceptions are narrow and disabled by default.
- Exactly one upstream authorization endpoint, token endpoint, MCP URL, and
  client ID are required.
- Exact callback URI and optional exact scope allowlists are supported without
  patterns, wildcards, or normalization surprises.
- Credential-mode validation permits only internally consistent `none`,
  `client_secret_post`, `client_secret_basic`, or `private_key_jwt` settings.
- Optional static upstream MCP headers are parsed once. Duplicate behavior is
  explicit, configured values are immutable, and the single authoritative
  forbidden-header set rejects unsafe names.
- Request-size, timeout, rate-limit, activity-timeout, and shutdown-drain values
  have documented safe defaults and bounds.
- Invalid configuration prevents readiness and startup with bounded errors that
  identify configuration keys but never secret values.
- `docs/configuration.md` documents the complete schema using fictional values.

**Required tests.**

- Unit tests for every required value, URL rule, redirect rule, scope rule,
  credential combination, numeric bound, header-name rule, duplicate rule, and
  local-development exception.
- Tests proving hostile forwarded host/scheme/header input cannot change
  canonical public or outbound URLs.
- Tests proving custom header values and credentials never appear in validation
  messages or configuration representations.
- Integration startup tests for one minimal valid configuration per credential
  mode and representative invalid configurations.
- Concurrency test proving resolved options do not mutate during requests.

---

## M2 — Safe telemetry, correlation, and health

**Scope.** Add structured JSON logging, central redaction, W3C tracing,
OpenTelemetry metrics/traces with optional OTLP export, bounded correlation,
health endpoints, and the telemetry test harness. No OAuth or proxy endpoint is
implemented in this milestone.

**Acceptance criteria.**

- Production logs are structured JSON on standard output; development has a
  documented concise formatter.
- Correlation IDs are accepted only when valid and bounded, otherwise generated,
  returned, and propagated through one shared implementation.
- Required baseline metrics and spans are registered with bounded labels.
- OTLP export is disabled when unconfigured and enabled without behavior changes
  when configured.
- `/health/live` reports process liveness and `/health/ready` reports local
  startup/configuration readiness without outbound dependency calls.
- One central redaction contract covers headers, configured values, OAuth query
  fields, forms, bodies, exceptions, spans, metrics, and health output.
- Telemetry failures cannot fail application requests or disclose data.
- Initial operational telemetry and safe-support-data guidance is documented.

**Required tests.**

- Unit tests for valid, invalid, oversized, and malicious correlation IDs.
- Snapshot/contract tests for log, span, metric, and health shapes.
- Canary-secret tests inject unique sensitive values through every available
  configuration and request surface and prove none appear in any telemetry sink,
  exception response, or health result.
- Tests prove metric labels remain bounded under arbitrary request input.
- Integration tests for OTLP configured/unconfigured behavior and exporter
  failure isolation.

---

## M3 — OAuth discovery metadata and MCP challenge

**Scope.** Implement bridge-owned protected-resource metadata,
authorization-server metadata, and the unauthenticated MCP bearer challenge.
Metadata is generated only from validated canonical configuration.

**Acceptance criteria.**

- `/.well-known/oauth-protected-resource` identifies the canonical `/mcp`
  resource, bridge authorization-server issuer, configured scopes, and bearer
  header use.
- `/.well-known/oauth-authorization-server` identifies the canonical issuer and
  bridge `/register`, `/authorize`, and `/token` endpoints.
- Authorization-server metadata advertises response type `code`, grants
  `authorization_code` and `refresh_token`, downstream client authentication
  `none`, and PKCE method `S256` only.
- An `/mcp` request without bearer authorization receives a standards-compatible
  `401` challenge pointing to the bridge protected-resource metadata.
- Metadata is cacheable using an explicit safe policy and never varies with Host,
  forwarded headers, caller identity, or arbitrary query input.
- Unsupported methods and content negotiation fail consistently without leaking
  configuration.
- Discovery and issuer behavior is documented with provider-neutral examples.

**Required tests.**

- Exact JSON contract tests for both metadata documents.
- Exact `WWW-Authenticate` contract tests, including safe encoding.
- Tests proving only `S256` and the specified grants/client authentication are
  advertised.
- Host/forwarding-header poisoning tests.
- Content-type, method, cache-header, malformed-path, and request-limit tests.
- Tests proving configured scopes are emitted exactly without translation.

---

## M4 — Stateless fixed-client DCR

**Scope.** Implement `POST /register` as a deterministic, storage-free RFC 7591
compatibility endpoint that validates downstream metadata and returns the one
configured upstream client ID as a public downstream client.

**Acceptance criteria.**

- Every registered redirect URI must exactly match the configured allowlist.
- Registration supports only response type `code`, grants
  `authorization_code`/`refresh_token`, and token authentication method `none`.
- Optional registration scope is validated when a scope allowlist exists and is
  otherwise preserved without translation.
- Valid registrations return the fixed client ID, no client secret, public-client
  metadata, and no registration-management credential or endpoint.
- Repeated equivalent registrations are deterministic and require no storage.
- Unsupported or malicious metadata receives standards-compatible bounded errors.
- DCR is protected by configured request-size and rate limits.
- DCR behavior and its confused-deputy controls are documented in the threat
  model and endpoint documentation.

**Required tests.**

- Contract tests for minimal and full valid registration documents.
- Repetition/restart tests proving the same valid registration works without
  persisted state and returns the fixed client ID.
- Exact callback matching tests covering scheme, host, port, path, case,
  encoding, fragments, multiple URIs, duplicates, and near matches.
- Negative tests for unsupported grants, response types, authentication methods,
  software metadata, malformed JSON, oversized JSON, and credential-smuggling
  fields.
- Scope preservation/allowlist tests.
- Rate-limit tests and concurrent registration tests.
- Canary-secret telemetry tests for registration bodies and errors.

---

## M5 — Transparent authorization forwarding and S256 PKCE

**Scope.** Implement `GET /authorize` validation and redirect forwarding to the
fixed upstream authorization endpoint. Preserve accepted query parameters
semantically while enforcing the fixed client, callback, scope policy,
authorization-code response type, and S256 PKCE.

**Acceptance criteria.**

- Only the configured client ID and exact allowed redirect URIs are accepted.
- `response_type=code`, a nonempty valid PKCE challenge, and
  `code_challenge_method=S256` are mandatory.
- `plain`, missing PKCE, duplicated security parameters, conflicting parameter
  values, and malformed encodings fail closed.
- Scope is forwarded unchanged and only rejected—not rewritten—when outside an
  optional configured allowlist.
- Accepted standard and extension parameters, including state, prompt, hints,
  and RFC 8707 `resource`, retain their semantic values and multiplicity unless
  the standard requires uniqueness.
- The redirect destination is always the configured upstream authorization
  endpoint and cannot be selected by input.
- The configured fixed client ID is used upstream; no provider-specific
  parameter is introduced.
- Authorization forwarding is rate limited, never retried, and never logs the
  query string or sensitive values.
- Direct callback and bridge/upstream issuer boundaries are documented.

**Required tests.**

- Exact redirect contract tests against a fake upstream authorization endpoint.
- Query preservation tests for encoding, Unicode, empty optional values,
  extension parameters, repeated permitted parameters, state, scope, challenge,
  and `resource`.
- Negative tests for client substitution, open redirect, callback near matches,
  response-type injection, PKCE downgrade, duplicate security parameters,
  malformed percent encoding, CRLF, and upstream-origin injection.
- Tests proving scope is unchanged byte-for-byte after standards-compliant form
  decoding/encoding semantics and is never silently added or removed.
- Rate-limit, cancellation, telemetry-redaction, and no-retry tests.

---

## M6 — Transparent token and refresh forwarding

**Scope.** Implement `POST /token` for authorization-code and refresh-token
grants with semantic form preservation, fixed upstream destination/client,
downstream credential-smuggling protection, and upstream authentication modes
`none`, `client_secret_post`, and `client_secret_basic`.

**Acceptance criteria.**

- Only `application/x-www-form-urlencoded` authorization-code and refresh-token
  requests within the configured limit are accepted.
- The downstream client ID must equal the fixed configured client ID.
- Authorization-code requests require an exact allowed redirect URI and a
  nonempty verifier; both are forwarded unchanged.
- Refresh tokens are forwarded once and never inspected, persisted, cached,
  replayed, or refreshed by the bridge.
- Scope and all permitted extension form fields are forwarded without semantic
  modification.
- Downstream secrets, assertions, and client-Authorization headers are rejected.
- The configured upstream authentication mode adds only the required client
  authentication and never exposes it downstream.
- Upstream token success and OAuth error statuses, content type, safe headers,
  and bodies are relayed without token substitution or schema translation.
- Token calls are time bounded, cancellation aware, rate limited, and never
  retried.
- Credential rotation through configuration-provider restart/redeployment is
  documented for secret modes.

**Required tests.**

- End-to-end fake-server tests for code exchange and refresh under `none`,
  `client_secret_post`, and `client_secret_basic`.
- Exact form-semantic contract tests for code, verifier, redirect URI, refresh
  token, scope, extension fields, empty optional fields, and valid repeated
  fields.
- Token-response pass-through tests with opaque/JWT-shaped access tokens,
  rotating refresh tokens, extension fields, and OAuth errors.
- Negative tests for content type, grant type, client substitution, redirect
  mismatch, missing verifier, duplicated security parameters, downstream Basic
  authentication, smuggled credentials/assertions, malformed forms, and body
  limits.
- Tests proving secrets are applied exactly once in the configured location.
- Timeout, cancellation, rate-limit, upstream-unavailable, no-retry, and
  telemetry canary-secret tests.

---

## M7 — Certificate-backed private-key JWT authentication

**Scope.** Add generic OAuth `private_key_jwt` client authentication using a
configured certificate/private key, without changing downstream public-client
or token pass-through behavior.

**Acceptance criteria.**

- Supported certificate/key sources and formats are explicitly configured,
  validated at startup, least-privilege, and documented.
- Assertions use the fixed client ID for issuer and subject, the exact configured
  token endpoint as audience, a short configured lifetime within safe bounds,
  UTC timestamps, a cryptographically random unique JWT ID, and an allowed
  signing algorithm.
- Each token or refresh request receives a fresh assertion.
- Private key material is never exported, logged, serialized, or exposed through
  errors/health/telemetry.
- Invalid, expired, not-yet-valid, key-usage-incompatible, or algorithm-
  incompatible certificates fail startup.
- Assertion generation is concurrency safe and cancellation aware.
- Certificate replacement by restart/redeployment and rollback are documented.

**Required tests.**

- Cryptographic contract tests that independently validate assertion signature,
  headers, issuer, subject, audience, JWT ID uniqueness, and lifetime.
- Fake token-server integration tests for authorization-code and refresh grants
  using `private_key_jwt`.
- Negative tests for wrong audience, invalid time bounds, missing private key,
  expired/not-yet-valid certificates, unsupported algorithms, bad key usage,
  and corrupted material.
- High-concurrency uniqueness/signing tests.
- Canary-private-key tests across logs, errors, traces, metrics, health, and
  process diagnostics exposed by the application.

---

## M8 — YARP MCP reverse proxy and configured headers

**Scope.** Implement `/mcp` reverse proxying to the one fixed upstream MCP URL
using YARP, bearer pass-through, exact path mapping, local discovery challenge,
upstream challenge rewriting, and the validated static MCP-header policy.

**Acceptance criteria.**

- `/mcp` maps only to the configured upstream URL; no request can change scheme,
  host, port, or base path.
- Bearer authorization is forwarded unchanged and is never validated, parsed,
  stored, or logged by the bridge.
- Missing bearer authorization receives the bridge discovery challenge without
  forwarding credentials elsewhere.
- Request/response methods, safe headers, status, content type, body, and MCP
  protocol headers are preserved subject only to HTTP hop-by-hop rules.
- Optional configured static headers are added only to upstream MCP requests,
  replace same-named downstream values case-insensitively, and never affect OAuth,
  discovery, DCR, token, or health requests.
- The single forbidden-header policy is enforced both at startup and forwarding.
- Upstream bearer `401` challenges identify bridge protected-resource metadata
  while preserving safe OAuth error and scope information.
- YARP does not buffer or interpret MCP bodies and performs no automatic retry.
- Proxy and custom-header behavior is fully documented with fictional examples.

**Required tests.**

- Integration tests for `GET`, `POST`, and `DELETE` proxying and exact upstream
  path/query mapping.
- Opaque bearer-token pass-through tests using canary tokens.
- Request/response header and status contract tests, including MCP session
  headers and hop-by-hop filtering.
- Static-header tests for zero/multiple headers, replacement, casing, multiple
  configured values, downstream spoofing, and isolation from non-MCP endpoints.
- Open-proxy/SSRF tests using absolute-form targets, Host/forwarding headers,
  path traversal, encoded paths, redirects, and malicious upstream responses.
- Upstream `401` challenge rewrite tests.
- No-buffer/no-retry tests and telemetry canary tests for bearer and configured
  header values.

---

## M9 — Streaming, session, cancellation, and failure semantics

**Scope.** Complete Streamable HTTP behavior under long-lived responses,
activity timeouts, client cancellation, upstream interruption, concurrent
sessions, graceful shutdown, and bounded failure handling.

**Acceptance criteria.**

- Streamable HTTP responses and server-sent events used within that transport are
  forwarded incrementally without whole-body buffering.
- `Mcp-Session-Id`, `Last-Event-ID`, accepted media types, and related MCP headers
  survive reconnect and subsequent request flows.
- Activity resets the configured streaming activity timeout; idle streams end
  predictably without imposing a fixed total duration.
- Client disconnect/cancellation promptly cancels upstream I/O and releases
  resources.
- Upstream connection, protocol, timeout, mid-stream, and cancellation failures
  map to documented behavior without replacing already-started response bodies.
- At least 100 concurrent active streams operate without cross-session leakage or
  unbounded memory growth on the documented reference environment.
- Graceful shutdown stops new work and drains/cancels active requests according
  to the configured bounded policy.
- Streaming and failure semantics are documented for operators.

**Required tests.**

- Incremental-delivery tests proving the first event reaches the client before
  the upstream response completes.
- Large/indefinite stream tests proving bounded memory behavior.
- Session-header and reconnect contract tests across concurrent sessions.
- Activity-timeout tests with active and idle streams.
- Client cancellation, upstream cancellation, abrupt disconnect, partial body,
  invalid response, and shutdown-drain tests.
- Concurrency/isolation tests at and above the specified 100-stream target.
- Tests proving no automatic retry or duplicate MCP side effect after any
  transport failure.

---

## M10 — Security and operational hardening

**Scope.** Complete the threat-model controls, endpoint rate limiting, HTTP
hardening, secret handling, bounded errors, dependency/security automation,
configuration diagnostics, and operator documentation across all implemented
surfaces.

**Acceptance criteria.**

- `docs/security.md` covers every threat required by `SPEC.md`, documents the
  mitigation and residual risk, and includes the direct-callback issuer
  assumption.
- DCR, authorization, and token endpoints have independent configurable bounded
  rate limits with safe responses and metrics.
- Server, request, response, forwarding, cookie, cache, content-sniffing, and TLS
  assumptions follow current ASP.NET Core and OAuth security guidance.
- All externally visible errors are bounded, correlated, protocol appropriate,
  and free of secrets and untrusted upstream detail.
- Production configuration diagnostics reveal presence/mode, never secret value.
- Dependency and container vulnerability checks run in the documented quality
  gate and have no unresolved high or critical findings.
- Credential and certificate rotation, incident response, safe support bundles,
  and rollback are documented in `docs/operations.md`.
- A focused security review finds no open high or critical issue.

**Required tests.**

- Complete negative security matrix for confused deputy, open redirect,
  authorization-server mix-up inputs, PKCE downgrade, credential smuggling,
  scope manipulation, SSRF, open proxying, header/request smuggling, forwarding
  spoofing, replay surfaces, request-size abuse, rate-limit bypass, and unsafe
  errors/logging.
- Fuzz/property-oriented tests for URI, query, form, JSON, and header parsing
  boundaries using deterministic seeds.
- Rate-limit partition, concurrency, recovery, and bounded-label tests.
- Security-header and cache-policy contract tests.
- Automated secret-canary sweep across all responses and telemetry artifacts.
- Dependency and container vulnerability scans.

---

## M11 — Packaging, performance, and release readiness

**Scope.** Produce the hardened OCI image, deployment/operations documentation,
repeatable performance evidence, full external interoperability acceptance
procedure, complete machine-readable endpoint documentation, and release audit.

**Acceptance criteria.**

- A minimal, non-root, reproducible OCI image is built from a pinned supported
  .NET runtime image, contains only runtime artifacts, exposes the documented
  port, honors graceful shutdown, and has working liveness/readiness probes.
- Container and local execution accept the same validated configuration contract
  and secret/certificate mounting model.
- `docs/deployment.md`, `docs/operations.md`, `docs/testing.md`, architecture and
  sequence diagrams, configuration reference, threat model, and README are
  complete and mutually consistent.
- Machine-readable and narrative endpoint documentation covers discovery, DCR,
  authorization, token, health, and MCP proxy behavior without exposing secrets.
- Repeatable benchmarks meet every latency, throughput, concurrency, streaming,
  and resource target in `SPEC.md`; methodology and reference environment are
  recorded.
- The documented manual external interoperability procedure verifies a real MCP
  client, OAuth authorization server, and MCP server, including DCR, S256,
  unchanged scopes, direct callback, original token response, downstream refresh,
  static MCP headers, RFC 9207 issuer compatibility, RFC 8707 `resource`, and
  Streamable HTTP.
- Any failed interoperability assumption triggers the SPEC-change protocol; it
  is not patched around during this milestone.
- A clean-checkout release build, all automated suites, coverage, analyzers,
  formatting, vulnerability audits, and final reviewer audit pass.

**Required tests.**

- Multi-stage OCI build and container smoke tests as an unprivileged user.
- Liveness/readiness, configuration failure, signal handling, and shutdown-drain
  tests in the container.
- Full unit, integration, contract, security, telemetry, streaming, and
  performance suites from a clean checkout.
- Repeatable non-streaming p95 latency test at 100 concurrent requests.
- At least 100 concurrent active-stream soak test with documented memory and CPU
  bounds.
- At least 100 requests/second OAuth/metadata benchmark while configured rate
  limits behave as documented.
- Documentation link/example validation and endpoint-schema validation.
- Manual external interoperability acceptance checklist with redacted evidence
  and no production credential captured in repository artifacts.
