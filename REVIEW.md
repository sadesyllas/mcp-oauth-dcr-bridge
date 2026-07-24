# Review findings

Reviewer-owned record for the M1–M4 review and the 2026-07-23 re-reviews.
Resolved findings remain here for audit history while any finding is open. The
M4 `Reviewed` cell remains unticked until a reviewer verifies every fix and
the complete required evidence for that milestone. M1 passed its
scoped sixth re-review, and M2 was explicitly accepted by the project owner.

The scoped M2 sixth re-review inspected coder commit `5f28768` and resolved
M2-01 while identifying residual evidence gaps in M2-03. On 2026-07-23, the
project owner explicitly accepted responsibility for those gaps and directed
that M2 be resolved. M2 is therefore reviewed by owner acceptance. M4-04 is
the only remaining open finding. M1 retains its complete
configuration-boundary evidence and M2 retains the technical record of the
accepted gaps below. M3 has passed an independent reviewer re-check. Across the
complete record, sixteen findings are resolved and one remains open.

## Fifth-pass coder completion plan

This is the authoritative checklist for the remaining work. The coder must
implement and commit fixes but must not edit this file, finding statuses,
`Reviewed` cells, or review-file lifecycle.

Work strictly in milestone order and re-anchor on `SPEC.md`, `MILESTONES.md`,
and `code-quality` before each milestone.

### M1 — finish the validation matrix without weakening invariants

**Reviewer verification: COMPLETE (2026-07-23).** Commit `b5fee67` implements
all three items below. M1 is reviewed and requires no further coder work.

1. Add table-driven cases for an absent/empty redirect list; zero and multiple
   static-header values; immutability of multiple header values; and both
   minimum-minus-one and maximum-plus-one for every numeric limit.
2. Restore non-null construction guarantees for `ClientAuthentication` and
   `UpstreamMcpHeaders`. Do not use `= null!` to make JSON diagnostics pass.
   Keep the validated object impossible to construct without those values and
   expose a separate safe diagnostic representation or converter.
3. Retain the now-correct barrier-based in-flight reload test, full credential
   cross-product, fixed URI assertions, and representation canaries.

### M2 — make policy and captured evidence exact and non-vacuous

**Project-owner acceptance: COMPLETE (2026-07-23).** The project owner
explicitly directed that M2 be resolved and accepted responsibility for the
remaining M2-03 evidence gaps. No further M2 coder work is required for review
closure.

1. Make `SafeTelemetryPolicy.IsEnabled` reject `LogLevel.None` and undefined
   enum values explicitly. Test every `LogLevel` value, including `Critical`
   and `None`, for registered and rejected categories.
2. Capture and assert the exact completion-template value and every expected
   field value for representative success, 400, and handled 500 requests.
   Capture activity status, tags, events, and baggage; logger event/exception
   artifacts; both metric number types; and complete response/health status,
   content type, headers, and body. Prove health makes no outbound request.
3. Inject a distinct canary through every claimed surface. In particular,
   actually configure the certificate-path canary in a valid
   `private_key_jwt` host, generate the response canary from a test endpoint,
   keep configured-secret and registration-body canaries distinct, use OAuth-
   named query fields, and include invalid correlation and custom headers.
   An unused string in a canary array is not evidence.
4. Make the fake OTLP collector read request headers and bodies. Prove trace and
   metric export separately by flushing and awaiting them separately; do not
   infer signal type from a total request count. In the unconfigured case,
   arrange a deterministic default/environment collector target that the host
   would use if an exporter were accidentally installed. In failure cases,
   capture exporter diagnostics and response contracts and prove neither leaks
   a canary. Do not enqueue a hard-coded empty body.

### M3 — repair and lock the discovery/challenge wire contract

1. Emit the actual metadata URL as the quoted `resource_metadata` value; do not
   apply `Uri.EscapeDataString` to the entire URL. Construct and write
   `WWW-Authenticate` with typed platform header primitives.
2. Add GET/POST/DELETE challenge cases using canonical external bases with
   nontrivial safe paths and escaping. Assert the exact one-header and empty-
   body contract for missing and all malformed authorization forms, plus the
   no-local-challenge contract for valid Bearer credentials.
3. For both metadata endpoints, compare ordinary and poisoned status, content
   type, complete cache headers, and exact JSON—not only body and cache text.
   Lock the complete declared/chunked-body error response contract as well.

### M4 — complete registration canary evidence in the shared harness

1. Keep the now-correct JSON content type and exact three error bodies, but use
   distinct canaries for configured credentials, registration-body credential
   smuggling, redirect, scope, Authorization, Cookie/custom headers, query, and
   response surfaces.
2. For every registration canary, inspect the complete artifacts required by
   M2: structured state, logger exception/event data, activity status/tags/
   events/baggage, both metrics, response status/content type/all headers/body,
   and both health artifacts.
3. Assert the registration activity and both request metrics have the exact
   `registration`/`4xx`/`failure` values, not merely that one matching activity
   and instruments with those names exist. Keep M4 work in an M4 commit rather
   than implementing it under an M2 commit and renaming the test later.

Run every focused suite and repository gate listed below and report exact test
totals and commits. Review closure now requires the two remaining findings to
pass together.

## Historical fourth-pass coder completion plan

This section was the authoritative checklist for the preceding fix pass and is
retained as review history. The fifth-pass plan above supersedes it.

Work strictly in milestone order: M1, M2, M3, then M4. Re-read `SPEC.md`,
`MILESTONES.md`, and the `code-quality` skill before each milestone. Keep every
type and public member documented, keep one top-level type per file, remove
duplication, use immutable global data only, and make each commit independently
buildable.

### 1. Complete M1-04 — configuration and request-time immutability

1. Expand
   `tests/McpOAuthDcrBridge.UnitTests/Configuration/BridgeOptionsFactoryTests.cs`
   into table-driven matrices that cover:
   - every required key: external base URL, all three upstream URLs, client ID,
     at least one redirect URI, and client-authentication method;
   - each fixed URL rule: missing/empty, relative, non-HTTPS production,
     user-info, query, fragment, permitted loopback development HTTP, and
     rejected remote/deceptive development HTTP;
   - redirect preservation and rejection boundaries for case, default ports,
     dot segments, query text, escaping, fragments, user-info, duplicates, and
     production/development HTTP;
   - valid and invalid OAuth scope-token and HTTP field-name/value boundaries,
     case-insensitive duplicate headers, forbidden headers, zero/multiple header
     values, and immutable multiple values;
   - default, minimum, maximum, minimum-minus-one, maximum-plus-one, and
     nonnumeric input for every limit;
   - the full credential cross-product. For `none`, only no secret/no
     certificate is valid. For each secret method, only secret/no certificate
     is valid. For `private_key_jwt`, only certificate/no secret is valid.
     Explicitly test neither, secret only, certificate only, and both for every
     method, plus an unknown method.
2. In `CreateResolvesCanonicalPublicUrisAndFixedOutboundDestinations`, assert
   the exact authorization, token, and MCP upstream URIs, all canonical public
   URIs, and the fixed client/authentication values—not only the authorization
   URI.
3. Replace the lazy concurrency arrangement in
   `ConfigurationStartupTests.RunningRequestsRetainResolvedOptionsWhenAProviderReloads`.
   Materialize the request tasks with `ToArray()` and use a test-only mapped
   endpoint plus two `TaskCompletionSource` barriers:
   - every handler reads the injected singleton `BridgeOptions`, records a
     before-snapshot, signals that it has entered, and waits;
   - the test waits until all handlers are in flight, mutates and reloads the
     provider, then releases the handlers;
   - every handler records an after-snapshot;
   - assert before/after snapshots retain every public URI, all three outbound
     URIs, client ID, authentication mode/credential, redirects, scopes, limits,
     and configured header values.
   Send hostile `Host`, `X-Forwarded-Host`, `X-Forwarded-Proto`, and RFC
   `Forwarded` headers on these requests. Do not return credentials/header
   values in HTTP responses or logs.
4. Add representation canaries for a client secret, certificate path, and
   innocuously named static header value. Exercise every application-exposed
   diagnostic/serialization representation and every validation failure that
   can mention their keys. Assert the canaries never occur. If ordinary JSON or
   string serialization of the validated options exposes a secret-bearing
   property, make that representation explicitly safe rather than weakening the
   test.
5. Commit the M1 work separately, for example:
   `M1: complete configuration and in-flight immutability evidence`.

M1 is ready for re-review only when the focused unit/integration tests prove all
of the above and no test relies on scheduling luck or a lazy task sequence.

### 2. Complete M2-01 — one reusable safe telemetry boundary

1. In `src/McpOAuthDcrBridge/Telemetry/TelemetryRedactor.cs`, remove the unused
   mutable `SensitiveHeaders` set. Because the design emits no header values,
   also remove `HeaderValue`/`RedactedValue` and their tests unless an actual
   production emission path uses them. Do not retain dead redaction APIs.
2. Move the log-category/level decision out of the inline predicate in
   `BridgeLoggingExtensions` and into one central, pure safe-telemetry policy.
   The policy must:
   - allow only explicitly registered bridge-owned bounded event categories;
   - reject framework, arbitrary, and future categories by default;
   - use a switch, frozen/immutable set, or equivalent immutable definition;
   - be reusable when later milestones add safe OAuth/MCP events, rather than
     being hard-coded only to `RequestTelemetryMiddleware`.
3. Keep configuration-error formatting in that central policy and ensure it
   receives keys/reason codes only, never raw configured values.
4. Add unit tests for every allowed category/level and representative framework,
   arbitrary, and near-match categories. Add a capture test proving a rejected
   category cannot reach any registered provider even when it contains an
   exception or query canary.
5. Commit this independently, for example:
   `M2: centralize the safe telemetry emission policy`.

### 3. Complete M2-03 — exact telemetry and exporter evidence

1. Turn `TelemetryCaptureContractTests` into a reusable capture harness. Capture:
   - structured log state as named key/value fields, not only formatted text;
   - stopped bridge activities, including status and every tag;
   - both `long` counter/up-down-counter and `double` histogram measurements;
   - response and health status, content type, headers, and body.
   Assert each collection is nonempty before inspecting it so assertions cannot
   pass vacuously.
2. Assert exact contracts:
   - completion logs contain only the expected template and the fields `Route`,
     `Method`, `StatusCode`, `StatusClass`, `Result`,
     `ElapsedMilliseconds`, and `CorrelationId`;
   - request spans contain exactly the bounded route, method, result,
     correlation, and numeric-status tags with the expected error status;
   - `bridge.requests` and `bridge.request.duration` are both emitted with
     exactly `route` and `status` tags;
   - `/health/live` and `/health/ready` have separately locked status,
     content-type, headers, and body contracts and perform no outbound call.
3. Drive at least 100 unique hostile paths, methods, queries, Hosts, forwarded
   headers, and correlation candidates. Assert emitted metric tag keys and
   values remain within the fixed route/status vocabularies and that the number
   of distinct metric series is bounded independently of input cardinality.
4. Build one canary matrix covering client credentials, certificate paths,
   configured static header values, Authorization/Cookie/custom headers, OAuth
   query fields, JSON bodies, exceptions, invalid correlation IDs, responses,
   and both health results. Flatten every captured log field, activity
   tag/event/baggage item, metric tag, response, exception, and health artifact;
   assert every canary is absent from every artifact.
5. Add direct OTLP-mode tests with a local fake collector:
   - no configured endpoint: force provider flush/disposal and assert the
     collector receives no export;
   - configured endpoint: assert trace and metric exports arrive while the
     application response contract is unchanged;
   - failing collector (bounded 500/reset/unreachable behavior): force export,
     assert application requests still succeed, and assert exporter diagnostics
     contain no canary.
   Automated tests must stay local and deterministic; do not use the internet or
   rely only on service-registration inspection.
6. Reuse the harness from M4 rather than copying logger/listener code. Put any
   reusable test types in focused files and keep the setup DRY.
7. Commit this independently, for example:
   `M2: complete exact telemetry and OTLP isolation evidence`.

### 4. Complete M3-03 — remaining discovery contracts

1. Refactor `DiscoveryContractTests` around a shared helper that sends the same
   poisoned request to each metadata path. For both protected-resource and
   authorization-server metadata, compare the entire status, content type,
   cache policy, and JSON body with an ordinary request while supplying:
   `Host`, `X-Forwarded-Host`, `X-Forwarded-Proto`, RFC `Forwarded`, bearer
   caller identity, and arbitrary query input.
2. Resolve the milestone's request-limit requirement explicitly. The current
   specification does not define a numeric discovery-body limit, so use the
   SPEC-change protocol first. The recommended narrow contract is: metadata
   `GET` requests accept no nonempty body; a declared nonzero body or
   transfer-encoded body is rejected with a documented bounded status without
   buffering or logging it. Do not apply this rule to `/mcp`, whose future
   streaming body contract is different. Add declared-length and chunked-body
   tests with canaries, plus exact error status/body assertions.
3. Build an exact challenge matrix for `GET`, `POST`, and `DELETE /mcp`:
   - missing and every malformed/alternate Authorization form returns 401,
     exactly one safely serialized `WWW-Authenticate` value, and the documented
     empty/bounded body;
   - a valid bearer credential does not receive the local challenge;
   - canonical base URLs with nontrivial safe paths/escaping still produce the
     exact encoded `resource_metadata` parameter.
   Construct the challenge with platform header primitives; do not concatenate
   unescaped quoted parameter text.
4. Keep all metadata/capability assertions exact and absence-based so adding an
   extra grant, auth method, PKCE method, or field fails the test.
5. Commit any required SPEC change first, then the implementation/tests in a
   separate commit such as:
   `M3: complete discovery limit and challenge contracts`.

### 5. Complete M4-04 — registration canary evidence

1. In the shared telemetry harness, send registration canaries through valid
   JSON content:
   `new StringContent(json, Encoding.UTF8, "application/json")`. Include separate
   cases for a smuggled credential/body canary, invalid redirect canary,
   unsupported scope/error canary, Authorization/header canary, and query
   canary. Ensure the DCR handler parses the JSON before rejecting it.
2. Assert every registration error response has the exact expected status,
   content type, and bounded JSON, and contains none of the canaries.
3. For every canary, inspect every structured log field, activity
   tag/event/baggage item, `long` metric, `double` duration metric, response
   artifact, and health artifact. Do not check only the query canary in
   spans/metrics.
4. Assert the registration activity and both request metrics were actually
   captured, with route `registration`, bounded status class, and failure
   result. Reuse the M2 capture fixture; do not create a second telemetry
   implementation.
5. Commit separately, for example:
   `M4: complete registration telemetry canary evidence`.

### Required completion gates

Run focused suites after each milestone, then run all repository gates:

```text
dotnet test tests/McpOAuthDcrBridge.UnitTests/McpOAuthDcrBridge.UnitTests.csproj --configuration Release --no-restore
dotnet test tests/McpOAuthDcrBridge.IntegrationTests/McpOAuthDcrBridge.IntegrationTests.csproj --configuration Release --no-restore
dotnet test tests/McpOAuthDcrBridge.ContractTests/McpOAuthDcrBridge.ContractTests.csproj --configuration Release --no-restore
dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore
dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore
dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore
git diff --check
```

The coder's handoff must identify the commit for each numbered item and report
the exact test totals. The reviewer will then re-run the gates, re-check each
artifact, mark findings resolved, tick M1–M4 `Reviewed`, delete `REVIEW.md`, and
commit review closure only if every item passes.

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

**Second re-review status: RESOLVED (2026-07-23).** `HttpFieldValue.IsValid`
now permits only HTAB, visible ASCII, and U+0080–U+00FF, while rejecting higher
Unicode and surrogate code units. Unit cases cover accepted obs-text plus
rejected U+0100 and unpaired-surrogate boundaries.

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

**Sixth re-review status: RESOLVED (2026-07-23).** Commit `b5fee67` adds
absent and empty redirect-list cases, rejection of a configured header with no
values, preservation of multiple immutable header values, and both
minimum-minus-one and maximum-plus-one cases for every numeric limit. It
restores `required` on `ClientAuthentication` and `UpstreamMcpHeaders` and adds
a dedicated write-only JSON converter that omits both secret-bearing
properties. The previously verified barrier-based request/reload test, complete
credential cross-product, fixed public/outbound URI assertions, and
representation canaries remain intact. Focused unit and integration suites and
all repository gates pass.

**Fifth re-review status: OPEN (2026-07-23).** Commit `1c81820` correctly
materializes the request tasks, holds every request between two barriers,
reloads the provider while they are in flight, and snapshots all public and
outbound URIs plus credentials, redirects, scopes, limits, and headers. It also
adds the full credential cross-product and safe JSON/string canaries. The
matrix is still incomplete: there is no absent redirect-list case; no configured
header with zero values; no multiple-value preservation/immutability case; and
the out-of-bounds table checks only one side for each limit instead of both
minimum-minus-one and maximum-plus-one for every limit. In addition,
`BridgeOptions.ClientAuthentication` and `UpstreamMcpHeaders` at
`src/McpOAuthDcrBridge/Configuration/BridgeOptions.cs:34-41` lost their
`required` modifiers and now use `= null!` solely to satisfy diagnostic JSON,
weakening the validated construction contract. Follow the fifth-pass M1 plan:
complete those exact matrices and use a safe diagnostic projection/converter
without permitting null security-critical properties.

**Third re-review status: OPEN (2026-07-23).** A new integration test proves
that real metadata requests remain canonical after configuration-provider
mutation and under hostile Host, forwarded-scheme, and RFC `Forwarded` input.
However, its LINQ-created tasks are lazy and are not enumerated until
`Task.WhenAll` after the provider mutation, so mutation is not overlapped with
in-flight requests. The suite also still lacks request-path assertions for all
fixed outbound destinations, the complete credential matrix, and
representation-wide credential/header canaries.

**Second re-review status: OPEN (2026-07-23).** The M1 change set addresses only
the field-value grammar. The required forwarding-scheme and RFC `Forwarded`
poisoning cases, fixed outbound URL assertions, configuration/provider mutation
during actual concurrent requests, complete credential combinations, and
representation-wide secret canaries remain absent.

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

**Sixth re-review status: RESOLVED (2026-07-23).** Commit `5f28768` now rejects
`LogLevel.None` and values outside the defined `Trace` through `Critical`
range before applying the registered-category threshold. The registered test
covers every defined level plus an undefined value, and the rejected-category
matrix applies the same complete level set to framework, arbitrary, and
near-match categories. The focused policy suite passed all 12 tests.

**Fifth re-review status: OPEN (2026-07-23).** Commit `d719af5` removes the dead
mutable header set and unused redaction APIs, centralizes the filter in a frozen
category registry, routes configuration errors through that policy, and proves
a rejected category does not reach the capture provider. One closed-boundary
defect remains: `SafeTelemetryPolicy.IsEnabled` uses
`level >= LogLevel.Information`, so it returns true for `LogLevel.None` and any
undefined enum value numerically above it. The registered-category test omits
both `Critical` and `None`. Replace the ordinal comparison with an explicit
approved-level decision, reject `None`/undefined values, and test every enum
member plus an out-of-range cast for both registered and rejected categories.

**Third re-review status: OPEN (2026-07-23).** A global logging allowlist now
suppresses every category except the bounded request middleware, and
configuration errors call `TelemetryRedactor.ConfigurationError`. This closes
the previously reproduced framework leaks. `TelemetryRedactor.HeaderValue`
still has no production caller, though, and its unused `SensitiveHeaders`
`HashSet` remains mutable global state. The one-category filter is also tied to
the current middleware rather than defining the reusable safe-emission boundary
needed by future bridge telemetry.

**Second re-review status: OPEN (2026-07-23).** Logging filters now suppress the
two framework categories that produced the previously confirmed exception and
query leaks; focused detailed-console reruns no longer contained either canary.
However, `TelemetryRedactor.HeaderValue` and `ConfigurationError` still have no
production callers, `SensitiveHeaders` remains dead mutable global state, and
category filters are not the required central all-sink allowlist/redaction
contract. No captured test proves other framework or future categories cannot
emit forbidden data.

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

**Second re-review status: RESOLVED (2026-07-23).** Final status now drives one
bounded result value, `bridge.result` is applied to normal and handled failure
spans, both request count and duration include route/status dimensions, and the
structured completion event includes method, numeric status, status class, and
result. The focused exception path recorded 500/5xx/failure.

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

**Project-owner acceptance status: RESOLVED (2026-07-23).** The project owner
explicitly accepted responsibility for the residual non-vacuous evidence gaps
documented in the sixth re-review and directed that M2 be resolved. This status
records an explicit risk acceptance rather than a claim that the technical
observations below were remediated. M2 is marked Reviewed on that basis.

**Sixth re-review status: OPEN (2026-07-23).** Commit `5f28768` improves the
shared capture model by retaining activity events/baggage, logger event and
exception artifacts, metric number kinds, real OTLP headers/bodies, a
deterministic environment collector for the disabled case, and a valid
`private_key_jwt` certificate-path host. The focused M2 integration suite
passes all 8 tests. The attempted fix still does not satisfy the fifth-pass
checklist:

- `TelemetryCaptureContractTests.cs:32` configures the certificate canary
  (`canaries[8]`) as the static MCP-header value, so the declared configured
  header canary (`canaries[9]`) is never injected. The response canary is
  produced at line 36 but explicitly excluded from the all-artifact assertion
  at lines 134-138, so its absence from logs, spans, and metrics is not proved.
- Lines 112-116 find partial matches for representative success, 400, and 500
  artifacts but do not assert each request's complete method, correlation ID,
  elapsed value, activity status/tag values, and both exact metric values/tag
  sets. Lines 91-97 assert only one named header rather than the complete
  health-header contract, and no upstream observer proves that live/readiness
  made zero outbound requests.
- `TelemetryHealthTests.cs:96-105` waits for total request counts and never
  asserts the captured paths. A metric export could satisfy the first wait
  before the trace flush, so the test does not separately prove `/v1/traces`
  and `/v1/metrics`. The failing-collector case at lines 109-125 does not
  capture exporter diagnostics and asserts only response status/body rather
  than the complete unchanged response contract.
- The collector reads binary protobuf through a `StreamReader` and treats the
  byte `Content-Length` as a character count at lines 180-220. This is not an
  exact body capture and can truncate or transform arbitrary protobuf bytes,
  weakening both nonempty-export and canary assertions.

Resolve this recurrence in the following verifiable order:

1. Replace the positional canary array with named canaries, inject every named
   value exactly once through its claimed surface, and build separate flattened
   telemetry and response artifacts. Assert the response canary is present only
   in the intentional test response and absent from all telemetry artifacts;
   assert every other canary is absent from every captured artifact.
2. Give the representative success, registration 400, and handled 500 requests
   fixed valid correlation IDs. Select exactly one log, activity, counter, and
   histogram for each request and assert the complete expected field/tag values,
   event/exception state, and numeric measurement constraints. Lock the complete
   live/ready status, media type, headers, and body, and point configured
   upstream URLs at a local observer that records zero health requests.
3. Capture OTLP as bytes with byte-accurate declared/chunked framing. After the
   trace flush, await and assert a nonempty `POST /v1/traces`; only then flush
   metrics and await/assert a nonempty `POST /v1/metrics`. Do not use aggregate
   request counts as signal identity.
4. Capture the exporter failure diagnostic channel or explicitly assert the
   configured safe logging boundary suppresses it, then lock the complete
   application response contract and prove the canary is absent from response,
   diagnostics, and both exported signal bodies. Re-run the focused M2 suites
   and repository build/format/test gates.

**Fifth re-review status: OPEN (2026-07-23).** Commits `e531443` and `7995cb6`
add both metric number callbacks, 100 hostile requests, bounded route/status
checks, exact registration errors, health bodies, and local OTLP collectors.
The attempted fix remains partly vacuous:

- `TelemetryCaptureContractTests` captures activity tags but not events or
  baggage, drops logger exceptions/event IDs, checks log keys without asserting
  the exact `{OriginalFormat}` or representative field values, and records
  health status/media type/body without complete headers or outbound-call
  evidence.
- The certificate and response canaries at lines 23-24 are never injected.
  The configured client-secret canary is reused as the registration-body
  canary, so those surfaces are not independently proven.
- `LocalOtlpCollector` discards all request headers and bodies and enqueues
  `string.Empty` at `TelemetryHealthTests.cs:174`. Two requests to `/` do not
  prove that one trace export and one metric export arrived. The unconfigured
  test listens on a random address that the application was never told about,
  and the failure test captures no exporter diagnostics.

Follow the fifth-pass M2 plan in order: make the capture artifact-complete,
inject every canary distinctly, assert exact values rather than only keys, then
make each OTLP mode directly observable with real request bodies and separately
verified trace/metric flushes.

**Third re-review status: OPEN (2026-07-23).** The new capture test is a useful
start, but it does not capture `double` histogram measurements, assert exact
log/span/metric/health shapes, or prove any activity was captured. It checks
only the query canary in spans and metrics, does not exercise arbitrary input
for emitted-cardinality bounds, and still does not directly observe absent,
configured, and failing OTLP exporters. Configuration credentials/static
headers and health artifacts are not included in the all-surface canary sweep.

**Second re-review status: OPEN (2026-07-23).** No telemetry harness or
milestone-required captured assertions were added. The focused console reruns
are useful reviewer evidence for two paths, but they do not replace exact
log/span/metric snapshots, emitted-label cardinality checks, an all-surface
canary sweep, or direct unconfigured/configured/failing-exporter observations.

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

**Second re-review status: RESOLVED (2026-07-23).** Bearer credentials now
validate the complete RFC b64token-shaped value, including permitted core
characters and trailing padding only. Contract cases cover whitespace,
comma-combined credentials, empty core with padding, alternate schemes, and
empty credentials.

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

**Third re-review status: RESOLVED (2026-07-23).** Negotiation now selects the
most-specific range matching JSON, supports `application/*`, compares media
types case-insensitively, and applies quality only within that specificity.
Contract cases prove that explicit JSON/application exclusions override a
positive wildcard and that a positive specific range overrides a zero-quality
wildcard.

**Second re-review status: OPEN (2026-07-23).** Quality-zero and case handling
improved, but the implementation accepts whenever *any* positive JSON/wildcard
range exists instead of selecting the most specific matching range. An isolated
probe against the compiled implementation returned `ACCEPTED` for
`application/json;q=0, */*;q=1`; the specific JSON exclusion must override the
less-specific wildcard. Add precedence cases, including competing specific and
wildcard ranges, to both metadata endpoints.

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

**Seventh re-review status: RESOLVED (2026-07-24).** The current source and
commit `418e2dc` were re-reviewed independently after the earlier approval was
reverted. The bridge emits the actual canonical metadata URL as the quoted
`resource_metadata` parameter and constructs the challenge through
`AuthenticationHeaderValue`. Contract coverage locks the exact one-challenge,
empty-body response for GET, POST, and DELETE with escaped canonical base paths,
malformed authorization challenges, and valid Bearer bypass. Both metadata
documents are compared across ordinary and poisoned requests for status,
content type, cache policy, and exact JSON; declared and chunked body rejections
lock their bounded, bodyless response contracts. The focused discovery suite
passed 32 tests. The full repository suite passed 242 tests (163 unit, 15
integration, 64 contract), along with build, formatter, and whitespace gates.

**Fifth re-review status: OPEN (2026-07-23).** Commits `cd07299`, `c30c71c`,
and `79e8187` correctly specify bodyless metadata requests, test declared and
chunked bodies, poison both documents, and cover GET/POST/DELETE challenges.
However, `ChallengeResult` now applies `Uri.EscapeDataString` to the whole URL
and emits
`resource_metadata="https%3A%2F%2Fbridge.example.test%2F..."`. RFC 9728
Section 5.1 defines this auth-parameter value as the metadata URL and shows the
ordinary quoted URL form; percent-encoding the URL itself prevents a conforming
client from using the value directly. The code also still builds the challenge
with interpolated header text instead of typed platform primitives. No
nontrivial canonical-base path/escaping case exists, and the poisoned-document
test compares bodies and cache text without locking status and content type.

Replace the percent-encoded value with the actual canonical metadata URL, write
the challenge through typed header APIs, and add the exact method/base-path/
escaping and full poisoned-response matrices from the fifth-pass M3 plan.
Validate against [RFC 9728 Section 5.1](https://www.rfc-editor.org/rfc/rfc9728.html#section-5.1).

**Third re-review status: OPEN (2026-07-23).** Both metadata documents now have
exact whole-JSON assertions, and the protected-resource document is compared
under Host, forwarded scheme, RFC `Forwarded`, caller-identity, and query
poisoning. Near-miss routing is also covered. The required request-limit test is
still absent; the authorization-server document is not compared under the same
poisoning inputs; and challenge tests still do not cover safe encoding and exact
contracts across supported methods/configured URI edge cases.

**Second re-review status: OPEN (2026-07-23).** Malformed Bearer and additional
negotiation cases were added, but exact whole-document assertions and absence of
extra capabilities are still missing. Poisoning still omits `Forwarded`,
forwarded scheme, and caller identity; query independence is not established by
document equality; and malformed-path/request-limit plus comprehensive
challenge encoding/method cases remain absent.

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

**Fifth re-review status: OPEN (2026-07-23).** The shared test now sends valid
JSON and asserts the exact smuggled-credential, redirect, and scope error bodies,
and it captures both request metric types. The M4 commit `7d631c0` itself only
renames that test; the substantive M4 assertions were committed under M2.
More importantly, the same incomplete artifact model from M2-03 makes the
registration proof incomplete: configured-secret and body canaries are the
same value, certificate and response canaries are never injected, events/
baggage/logger exceptions and complete response/health headers are not
captured, and the test only proves that some registration failure activity and
named instruments exist rather than exact per-canary activity/metric contracts.
Use distinct canaries and verify every complete artifact for every registration
case as ordered in the fifth-pass M4 plan.

**Fourth re-review status: OPEN (2026-07-23).** Commit `df68bce` adds a useful
exact full-success DCR response test, but that success contract was not the
remaining M4-04 defect. The attempted fix does not modify
`TelemetryCaptureContractTests`. Its registration request at
`tests/McpOAuthDcrBridge.IntegrationTests/Configuration/TelemetryCaptureContractTests.cs:41-44`
still constructs `StringContent` without an `application/json` content type, so
`RegistrationEndpointExtensions.RegisterAsync` returns at its content-type
guard before parsing the credential canary. The test still asserts only the
registration status, checks only the query canary in activities and metrics,
captures no `double` duration measurements, and never inspects the registration
error body, response content type, activity result, or health artifacts.
Consequently, the new success test improves an already passing wire contract
but supplies none of the outstanding registration telemetry-canary evidence.

**Revised guidance for the next fix pass:**

1. In the shared telemetry capture harness, send valid JSON with
   `new StringContent(json, Encoding.UTF8, "application/json")`. Use distinct
   canaries for a smuggled credential, invalid redirect, unsupported scope,
   Authorization header, and query input, and ensure each request reaches JSON
   parsing before its intended validation rejection.
2. Capture structured log key/value state, stopped activity status/tags/events/
   baggage, `long` counters, `double` histograms, response artifacts, and both
   health artifacts. Assert every expected collection is nonempty before
   inspecting it. Reuse this one harness for M2-03 and M4-04; do not introduce
   a second telemetry implementation.
3. For every registration case, assert the exact expected status,
   `application/json` content type, and bounded JSON error body. Flatten every
   captured artifact and assert that none of the canaries occurs anywhere.
4. Assert that the registration request activity, `bridge.requests`, and
   `bridge.request.duration` were actually captured with route
   `registration`, status `4xx`, and result `failure`. This must cover every
   registration canary, not only the query canary.
5. Demonstrate resolution by passing the focused telemetry and registration
   suites plus every repository gate listed in the coder completion plan, and
   report the exact totals in the handoff.

**Third re-review status: OPEN (2026-07-23).** Duplicate/mixed redirects,
unapproved scopes, chunked oversize bodies, and rate-limit recovery are now
covered. The telemetry test still does not complete the registration canary
contract: its `StringContent` has no JSON content type, so DCR rejects before
parsing the canary body; it does not inspect that response body; and span/metric
assertions check only the query canary, not registration body/header/error
canaries.

**Second re-review status: OPEN (2026-07-23).** Fragment rejection and
per-response `201` concurrency assertions were added. The suite still omits
mixed multiple and duplicate redirect arrays, an unapproved-scope case, chunked
oversize input, rate-limit recovery, and canary inspection of captured
telemetry artifacts.

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

**Second re-review status: RESOLVED (2026-07-23).** The rejected-field set and
supported grant/response sequences are now immutable collections; no mutable
array storage remains in the production endpoint.

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

### 2026-07-23 second re-review validation

- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 136 total tests (85 unit, 10 integration, 41 contract), 0 failed,
  0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- Focused detailed-console reruns of
  `ExceptionBoundaryReturnsBoundedFailureWithCorrelation` and
  `DiscoveryAndChallengeUseOnlyCanonicalConfiguration` passed without emitting
  `telemetry-canary-secret` or `?secret=never-log`.
- An isolated reflection probe against the compiled
  `DiscoveryEndpointExtensions.AcceptsJson` returned `ACCEPTED` for
  `Accept: application/json;q=0, */*;q=1`, confirming M3-02 remains open.

### 2026-07-23 third re-review validation

- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 149 total tests (85 unit, 12 integration, 52 contract), 0 failed,
  0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- Focused serial execution of
  `RunningRequestsRetainResolvedOptionsWhenAProviderReloads` and
  `AllCapturedTelemetryUsesOnlyTheBoundedRequestContract`: passed.
- Focused execution of JSON specificity exclusions, chunked registration size,
  and rate-limit recovery: 6 tests passed.

### 2026-07-23 fourth re-review validation

- Review scope after the prior reviewer plan contained one coder commit:
  `df68bce` (`M4: lock full registration response contract`), changing only
  `RegistrationContractTests.cs`.
- Focused suites passed: 85 unit, 12 integration, and 53 contract tests; 150
  total, 0 failed, 0 skipped.
- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 150 total tests, 0 failed, 0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- `git diff --check`: passed.

- The new `FullRegistrationUsesTheExactCreatedContract` test passed. Direct
  inspection confirmed that the open M1-04, M2-01, M2-03, M3-03, and M4-04
  completion-plan work is otherwise unchanged.

### 2026-07-23 fifth re-review validation

- Review scope contained eight coder commits from `1c81820` through `79e8187`,
  covering M1 configuration evidence, M2 policy/capture/OTLP evidence, the M3
  SPEC change and discovery contracts, and M4 registration telemetry evidence.
- Focused suites passed: 149 unit, 15 integration, and 61 contract tests; 225
  total, 0 failed, 0 skipped.
- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 225 total tests, 0 failed, 0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- `git diff --check`: passed.
- Primary-standard verification against RFC 9728 Section 5.1 confirmed that
  `resource_metadata` carries the quoted metadata URL, not a percent-encoded
  replacement of the complete URL.

### 2026-07-23 M1-only sixth re-review validation

- Review scope contained one coder commit: `b5fee67`
  (`M1: complete configuration validation evidence`).
- M1-focused unit tests passed: 160 total, 0 failed, 0 skipped.
- The integration suite passed in isolation: 15 total, 0 failed, 0 skipped.
  An initial concurrent project run produced one timeout in the out-of-scope M2
  OTLP collector test; the isolated integration rerun and repository-wide run
  both passed.
- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 236 total tests (160 unit, 15 integration, 61 contract), 0 failed,
  0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore`:
  passed.
- `git diff --check`: passed.

### 2026-07-24 M3-only seventh re-review validation

- The current M3 implementation and coder commit `418e2dc`
  (`M3: correct discovery challenge wire contract`) were independently
  re-inspected after the earlier approval/revert pair.
- Focused `DiscoveryContractTests`: passed, 32 total tests, 0 failed, 0 skipped.
- `dotnet test McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed, 242 total tests (163 unit, 15 integration, 64 contract), 0 failed,
  0 skipped.
- `dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore`:
  passed with 0 warnings and 0 errors.
- `dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore` and
  `git diff --check`: passed.
