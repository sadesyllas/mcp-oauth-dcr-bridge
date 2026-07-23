# Review findings

Reviewer-owned record for the M1–M4 review and the 2026-07-23 re-review.
Resolved findings remain here for audit history while any finding is open. The
`Reviewed` cells for M1–M4 must remain unticked until a reviewer verifies every
fix and the complete required evidence.

The re-review found 10 open findings: nine partially addressed original
findings and one new code-quality finding. Seven original findings are resolved.

## M1 — Immutable configuration and trust boundaries

### M1-01 — Local-development HTTP is not limited to local hosts (high)

**Re-review status: RESOLVED (2026-07-23).** HTTP is now accepted only for
`localhost` or an IP address recognized as loopback, and the suite covers remote
DNS, IPv4, IPv6, and deceptive localhost inputs.

**Where:** `src/McpOAuthDcrBridge/Configuration/BridgeOptionsFactory.cs:178`

`AllowHttpForLocalDevelopment` changes the URI scheme check for every configured
URI, but `ParseUri` never verifies that an HTTP URI is loopback. In a Development
host, the flag therefore accepts such values as `http://remote.example.test`,
contrary to the milestone's narrow local-development exception and the
loopback-only behavior documented in `docs/configuration.md`.

**Guidance:** Give the development exception an explicit loopback-host rule and
apply it consistently to each URI category. Add negative coverage for remote
IPv4, IPv6, DNS, user-info, and deceptive-loopback inputs in both Development
and non-Development environments.

### M1-02 — Scope and HTTP-header grammars are not validated (high)

**Re-review status: OPEN (2026-07-23).** OAuth scope-token and HTTP field-name
validation are fixed. `HttpFieldValue.IsValid` at
`src/McpOAuthDcrBridge/Configuration/HttpFieldValue.cs:11` still accepts every
Unicode code point above U+007F, including values outside HTTP `obs-text`
(octets 0x80–0xFF) and unpaired surrogates. The tests cover CR/LF but no Unicode
or surrogate boundaries. Constrain values to HTAB, SP/VCHAR, and the explicitly
supported obs-text range, then prove the forwarding representation accepts
every permitted value.

**Where:** `src/McpOAuthDcrBridge/Configuration/BridgeOptionsFactory.cs:97-125`
and `:194`

Scope validation rejects whitespace but accepts characters excluded by the OAuth
`scope-token` grammar, including quotes, backslashes, controls, and arbitrary
Unicode. Header-name validation accepts nearly every visible ASCII character
other than `:`, including separators that are invalid in an HTTP field-name.
Configured header values are checked only for emptiness, so CR/LF and other
invalid values reach the forwarding layer instead of failing startup.

**Guidance:** Validate OAuth scope tokens and HTTP field names/values with their
standards-defined grammars and platform primitives, while retaining the single
authoritative forbidden-name set. Add exhaustive boundary and canary tests for
names, values, and scope tokens.

### M1-03 — Redirect configuration is normalized and rejects valid query-bearing callbacks (high)

**Re-review status: RESOLVED (2026-07-23).** Redirect validation is separate
from fixed endpoint validation; query-bearing redirect strings are retained for
ordinal comparison without `Uri.AbsoluteUri` replacement.

**Where:** `src/McpOAuthDcrBridge/Configuration/BridgeOptionsFactory.cs:76-94`
and `:178-185`

Redirect URIs share the generic endpoint parser, which rejects every query
component even though OAuth redirect URIs may contain one. Accepted values are
then replaced with `Uri.AbsoluteUri`, so host casing, default ports, dot
segments, and escaping can be normalized before later ordinal comparison. This
does not preserve the exact configured callback contract and creates the
normalization surprises M1 explicitly forbids.

**Guidance:** Separate redirect validation from outbound-endpoint validation.
Reject fragments and unsafe/malformed values, permit standards-valid query
components, retain one explicitly defined canonical/exact comparison contract,
and cover case, port, path, query, percent-encoding, Unicode, and near-match
behavior.

### M1-04 — The required M1 test matrix is incomplete (high)

**Re-review status: OPEN (2026-07-23).** Boundary coverage expanded, but
`BridgeOptionsFactoryTests` still does not test every required value or all
credential combinations. The poisoning contract covers Host and
`X-Forwarded-Host`, not forwarded scheme or RFC `Forwarded`, and it checks only
public metadata rather than all fixed outbound destinations. The provider
mutation test still performs direct option reads rather than concurrent HTTP
requests, so it does not prove immutability *during requests*. Configuration
representations also are not swept for credential/header canaries.

**Where:** `tests/McpOAuthDcrBridge.UnitTests/Configuration/BridgeOptionsFactoryTests.cs:8-150`
and `tests/McpOAuthDcrBridge.IntegrationTests/Configuration/ConfigurationStartupTests.cs`

The suite samples a few invalid values but does not cover every required key,
URI rule, scope rule, credential combination, numeric default/minimum/maximum
and parse failure, header grammar, or local-development boundary. There is no
request-level hostile Host/scheme/forwarding-header test. The concurrency test
only reads one object from worker tasks; it neither mutates the underlying
configuration provider nor performs concurrent requests, so it does not prove
the stated acceptance criterion.

**Guidance:** Build a table-driven validator matrix and add integration tests
that issue hostile requests against a running host. Mutate a reload-capable
configuration source while concurrent requests resolve/use the singleton
options, and prove public and outbound URIs plus secret-bearing settings do not
change or leak.

## M2 — Safe telemetry, correlation, and health

### M2-01 — The central redaction contract is unused and incomplete (high)

**Re-review status: OPEN (2026-07-23).** Bounded application-owned dimensions
were added, but framework logging still bypasses them. A focused run of
`ExceptionBoundaryReturnsBoundedFailureWithCorrelation` emitted the complete
exception containing `telemetry-canary-secret` from
`Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware`. A focused
discovery run emitted the complete `?secret=never-log` query in both request
start and finish logs from `Microsoft.AspNetCore.Hosting.Diagnostics`.
`TelemetryRedactor.HeaderValue` and `ConfigurationError` remain unused, and
`SensitiveHeaders` is now dead mutable state. Configure or filter framework
categories so raw queries, exception objects, and other forbidden values cannot
reach any sink, and verify captured output rather than only response bodies.

**Where:** `src/McpOAuthDcrBridge/Telemetry/TelemetryRedactor.cs:6-25`

No production telemetry path calls `TelemetryRedactor`. Its two methods cover
only a short header-name list and configuration-key text; they do not cover
configured header values, OAuth query/form/body fields, exceptions, spans,
metrics, or health output. In particular, a configured static header whose name
looks innocuous is a secret by specification but `HeaderValue` would return its
value unchanged.

**Guidance:** Route every diagnostic emission through one safe telemetry model
that allowlists bounded fields rather than attempting ad hoc value cleanup.
Represent all configured secret values as sensitive regardless of header name,
install a bounded exception/error policy, and add a canary sweep over logs,
spans, metrics, responses, and health output.

### M2-02 — Required telemetry is missing and request outcomes can be wrong (high)

**Re-review status: OPEN (2026-07-23).** The complete bridge instrument catalog,
runtime instrumentation, endpoint classification, and handled 500 status are
now present. However, normal and exception-handler-consumed 4xx/5xx spans never
receive `bridge.result`; the request-duration histogram at
`RequestTelemetryMiddleware.cs:52` omits status class; and the structured
completion log records only status class, not the required numeric status.
Complete the shared result model and apply the same bounded route/status/result
dimensions to logs, spans, counts, and durations.

**Where:** `src/McpOAuthDcrBridge/Telemetry/BridgeTelemetry.cs:9-21` and
`src/McpOAuthDcrBridge/Telemetry/RequestTelemetryMiddleware.cs:25-58`

Only inbound count and duration instruments exist. Required upstream OAuth/MCP
counts and durations, validation-rejection counts, proxy failure/timeout
metrics, active MCP requests/streams, and process/runtime metrics are not
registered. Request classification knows only the two health paths, so
discovery and DCR are recorded as `other` and the log omits required method and
result fields. When downstream middleware throws, the `finally` block observes
the response's pre-error status (commonly 200), causing a failed request to be
logged and measured as successful.

**Guidance:** Define the complete bounded instrument catalog now, add supported
ASP.NET Core/runtime instrumentation, and centralize endpoint/result
classification. Record exceptions as bounded failure categories and observe the
final status produced by a safe exception boundary.

### M2-03 — The required telemetry evidence is absent (high)

**Re-review status: OPEN (2026-07-23).** Helper-level bounded-dimension tests and
one unreachable OTLP endpoint test were added, but there are still no captured
exact log/span/metric contracts, no emitted-metric cardinality test under
arbitrary input, and no canary sweep across actual logs, spans, metrics, health,
configuration, exceptions, queries, headers, and bodies. Unconfigured exporter
absence and exporter failure isolation are not directly observed. The current
focused runs demonstrate that the missing log assertions conceal real leaks.

**Where:** `tests/McpOAuthDcrBridge.IntegrationTests/Configuration/TelemetryHealthTests.cs:7-22`
and `tests/McpOAuthDcrBridge.UnitTests/Configuration/CorrelationIdentifierFactoryTests.cs`

M2 has correlation examples and two health requests, but no exact log, span, or
metric shape tests; no arbitrary-input bounded-label test; no canary-secret
sweep; no OTLP enabled/disabled integration test; and no exporter-failure
isolation test. Health response bodies and the distinction between liveness and
local readiness are not asserted as contracts.

**Guidance:** Add in-memory telemetry sinks/exporters and snapshot the bounded
contracts. Exercise all available configuration/request surfaces with unique
canaries, hostile cardinality input, both OTLP modes, a failing exporter, and
separate liveness/readiness assertions.

## M3 — OAuth discovery metadata and MCP challenge

### M3-01 — Non-Bearer authorization suppresses OAuth discovery (high)

**Re-review status: OPEN (2026-07-23).** Basic and empty Bearer cases now
challenge, but `HasBearerCredential` at
`DiscoveryEndpointExtensions.cs:17-22` validates only the first character after
`Bearer `. Credentials containing whitespace or invalid b64token characters,
and comma-combined duplicate credentials, can still suppress discovery. Parse
the full RFC bearer credential grammar and extend the malformed/duplicate
matrix.

**Where:** `src/McpOAuthDcrBridge/Discovery/DiscoveryEndpointExtensions.cs:18`

The `/mcp` handler checks only whether the `Authorization` header collection is
empty. A request carrying Basic, Digest, or another non-Bearer scheme is treated
as authenticated enough to suppress discovery and receives 404. The milestone
requires the bearer challenge whenever bearer authorization is absent.

**Guidance:** Parse the authorization scheme safely and require a well-formed,
nonempty Bearer credential before yielding to the future proxy. Challenge all
missing, alternate, empty, duplicate, and malformed authorization cases without
logging credential material.

### M3-02 — Unsupported content negotiation is accepted (medium)

**Re-review status: OPEN (2026-07-23).** A 406 path exists, but `AcceptsJson` at
`DiscoveryEndpointExtensions.cs:15` ignores quality values and compares media
types case-sensitively. It accepts `application/json;q=0` and can reject a
case-variant JSON media type. Use typed quality-aware, case-insensitive matching
and cover multiple ranges, wildcards, and explicit exclusions.

**Where:** `src/McpOAuthDcrBridge/Discovery/DiscoveryEndpointExtensions.cs:16-17`
and `src/McpOAuthDcrBridge/Discovery/DiscoveryResult.cs`

The metadata endpoints ignore `Accept` and always return JSON. This does not
implement the acceptance criterion that unsupported content negotiation fail
consistently.

**Guidance:** Define and implement one bounded negotiation policy, return the
appropriate failure for unsupported media types, and apply it consistently to
both metadata documents.

### M3-03 — The required discovery contract matrix is missing (high)

**Re-review status: OPEN (2026-07-23).** Coverage expanded to scopes, one Host
poison, 406, 405, and content type, but neither metadata test asserts the exact
whole JSON document and absence of extra capabilities. Poisoning omits
`Forwarded`, forwarded scheme, and caller identity; query independence is not
proved by comparing documents; and malformed-path/request-limit plus complete
challenge-encoding/malformed-Bearer cases remain absent.

**Where:** `tests/McpOAuthDcrBridge.ContractTests/DiscoveryContractTests.cs:8-23`

The only test checks one substring, one cache header, and one challenge. It does
not assert either exact JSON document, configured scopes, exclusive advertised
capabilities, hostile Host/forwarded headers, caller identity/query
independence, content type/negotiation, unsupported methods, malformed paths,
request limits, or challenge encoding and alternate authorization schemes.

**Guidance:** Replace substring checks with exact semantic JSON and header
contracts, then add the full poisoning, routing, negotiation, method, path,
scope, and authorization-scheme matrices from M3.

### M3-04 — Endpoint composition uses service location (medium)

**Re-review status: RESOLVED (2026-07-23).** The validated options are now an
explicit mapping dependency supplied by the composition root.

**Where:** `src/McpOAuthDcrBridge/Discovery/DiscoveryEndpointExtensions.cs:15`

The endpoint mapper pulls `BridgeOptions` from `application.Services`. The
specification explicitly forbids service locators and requires explicit
dependencies.

**Guidance:** Let minimal-API dependency injection supply `BridgeOptions` to
handlers or inject a focused discovery service. Apply the same pattern to the
registration endpoint noted in M4-05.

## M4 — Stateless fixed-client DCR

### M4-01 — The successful/error DCR wire contract is not RFC 7591-compatible (high)

**Re-review status: RESOLVED (2026-07-23).** Success now returns 201, absent
scope is omitted, and redirect mismatch returns a bounded
`invalid_redirect_uri` contract with exact tests.

**Where:** `src/McpOAuthDcrBridge/Registration/RegistrationEndpointExtensions.cs:55-63`
and `:99`

`Results.Json` defaults a successful registration to HTTP 200, while RFC 7591
registration success uses 201 Created. A minimal request serializes
`"scope": null`, even though `scope` is string-valued metadata and should be
omitted when absent. Every failure also returns `invalid_client_metadata`;
redirect failures should use the protocol's redirect-specific error.

**Guidance:** Introduce explicit response/error contracts, return 201 for
success, conditionally omit absent optional metadata, map bounded RFC error
codes by failure category, and lock status, content type, headers, and exact JSON
with contract tests.

### M4-02 — Capability and scope validation accepts malformed metadata (high)

**Re-review status: RESOLVED (2026-07-23).** Duplicate JSON properties,
empty/duplicate capability arrays, malformed scopes, and unsupported values are
now rejected through the shared OAuth scope-token grammar.

**Where:** `src/McpOAuthDcrBridge/Registration/RegistrationEndpointExtensions.cs:41-81`

Empty and duplicate `response_types` or `grant_types` arrays pass validation.
`ScopeAllowed` uses `RemoveEmptyEntries`, so empty, leading/trailing, and
repeated-space scopes can pass; with no allowlist it accepts any string,
including values outside the OAuth scope grammar. Duplicate JSON member names
are also not rejected, leaving security-relevant metadata ambiguous.

**Guidance:** Validate one unambiguous JSON object against explicit supported
metadata rules, reject duplicate security-relevant members and empty/duplicate
capability arrays, and share the authoritative OAuth scope parser from M1 while
preserving accepted scope text exactly.

### M4-03 — The required confused-deputy threat-model update is missing (high)

**Re-review status: RESOLVED (2026-07-23).** `docs/security.md` now records the
DCR threats, mitigations, and residual risks, and `docs/registration.md` links
to it.

**Where:** `docs/registration.md`

The endpoint note lists a few rejected fields, but the milestone explicitly
requires the controls in the threat model as well as endpoint documentation.
There is no `docs/security.md` or equivalent threat-model artifact covering
malicious DCR metadata, redirect manipulation, credential smuggling, replay,
denial of service, and residual risk.

**Guidance:** Add the required threat-model section/artifact and link it from the
registration documentation. Keep the mitigations and residual risks aligned
with the implemented validation, limits, telemetry, and stateless behavior.

### M4-04 — The required registration test matrix is missing (high)

**Re-review status: OPEN (2026-07-23).** Minimal success, redirect error,
restart, declared size, and basic concurrency contracts were added. The suite
still omits fragment/multiple/duplicate redirect requests, an unapproved-scope
case, chunked oversize input, rate-limit recovery, and telemetry artifact
canaries. `ConcurrentValidRegistrationsRemainStateless` collects bodies without
asserting that each response was 201, so identical error bodies would satisfy
the test.

**Where:** `tests/McpOAuthDcrBridge.ContractTests/RegistrationContractTests.cs:8-55`

The three tests do not provide minimal/full exact contracts, restart evidence,
the callback scheme/host/port/path/case/encoding/fragment/multiple/duplicate
matrix, capability/auth/software/malformed/oversized negative cases, scope
allowlist/preservation coverage, concurrent registration, or telemetry canary
evidence. Repeating a request in one process does not prove restart/stateless
behavior.

**Guidance:** Add table-driven unit and contract cases for the full M4 list,
restart the host between deterministic requests, test chunked and declared
oversize bodies, exercise rate-limit recovery/concurrency, and inspect telemetry
artifacts for request-body/error canaries.

### M4-05 — Registration composition uses service location (medium)

**Re-review status: RESOLVED (2026-07-23).** Registration mapping now receives
the validated options explicitly from the composition root.

**Where:** `src/McpOAuthDcrBridge/Registration/RegistrationEndpointExtensions.cs:20`

Like M3, the mapper resolves `BridgeOptions` manually from the application
service provider, contrary to the explicit-dependency and no-service-locator
coding standard.

**Guidance:** Supply the options through handler dependency injection or a
focused registration service, and keep mapping limited to route composition.

### M4-06 — Registration protocol constants use mutable global arrays (medium)

**Re-review status: OPEN (new on 2026-07-23).**

**Where:** `src/McpOAuthDcrBridge/Registration/RegistrationEndpointExtensions.cs:12-14`

`RejectedFields`, `SupportedGrants`, and `SupportedResponseTypes` are static
arrays. `readonly` protects only each reference, not the array contents, so the
production type retains mutable global state contrary to the specification.

**Guidance:** Use immutable/frozen collections with the appropriate ordinal
comparers, and serialize immutable response values without exposing mutable
shared storage.

## Validation performed by the reviewer

- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 46 total tests, 0 failed, 0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.

These gates demonstrate that the current implementation is internally green;
they do not replace the milestone-specific tests listed above.

### 2026-07-23 re-review validation

- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 124 total tests, 0 failed, 0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- Focused detailed-console execution of
  `ExceptionBoundaryReturnsBoundedFailureWithCorrelation`: passed functionally
  but exposed `telemetry-canary-secret` in the JSON exception log.
- Focused detailed-console execution of
  `DiscoveryAndChallengeUseOnlyCanonicalConfiguration`: passed functionally but
  exposed `?secret=never-log` in request-start and request-finish JSON logs.
