# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M5 — Transparent authorization forwarding and S256 PKCE

### M5-1 (required-test gap): no telemetry-redaction coverage for the `/authorize` input surface

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.IntegrationTests/Configuration/TelemetryCaptureContractTests.cs`
(the canary/input-surface suite) and, if needed,
`tests/McpOAuthDcrBridge.ContractTests/AuthorizationContractTests.cs`.

**Problem.** M5's required tests include "Rate-limit, cancellation,
**telemetry-redaction**, and no-retry tests", and its acceptance criteria state
the endpoint "never logs the query string or sensitive values". No test
exercises `/authorize` with canary values and proves their absence from
telemetry. Specifically:

- `TelemetryCaptureContractTests` never sends a request to `/authorize`. Its
  input-surface inventory (`CreateInputSurfacesAsync` / `TestCanaries.InputSurfaceNames`)
  — the mechanism M4 established precisely so every new input surface must
  enroll — has no entries for authorization query values (`state`,
  `code_challenge`, `scope`, `redirect_uri`, extension parameters).
- The route-allowlist assertions in that suite (`allowedRoutes`,
  the `capture.Logs` route assertions) therefore never observe the new
  `authorization` route classification under real traffic, even though
  `TelemetryEndpointClassifier` now emits it.

The redaction behavior is *probably* correct because the shared
`RequestTelemetryMiddleware` logs only bounded fields, but M5 explicitly
requires the evidence, and the canary-inventory contract exists to make new
surfaces impossible to skip. This surface was skipped.

**Guidance.**

1. Add canary values for the authorization surface (at minimum `state`,
   `code_challenge`, and a query `scope`; a disallowed `redirect_uri` canary is
   also valuable since it is echoed nowhere but travels through the rejection
   path) to `TestCanaries` and `CreateInputSurfacesAsync`, so
   `AssertInputSurfaces` fails if the surface is ever dropped.
2. In the same capture test, issue at least one **valid** `/authorize` request
   (canary state/challenge/scope, expecting `302`) and one **rejected** request
   (e.g. canary near-miss `redirect_uri`, expecting `400`), then assert:
   - the canaries appear in no log entry, activity, or measurement
     (`AssertCanariesAreAbsent` over the flattened artifacts) — the redirect
     `Location` response header is legitimately allowed to carry them and must
     be excluded the same way `canaries.Response` is excluded today;
   - the request log/metric for these calls carries route `authorization` with
     only the bounded fields already asserted for other routes (extend
     `allowedRoutes` accordingly).
3. Result that demonstrates resolution: the capture suite fails if an
   `/authorize` canary leaks into any telemetry sink, and fails if the
   authorization surface is removed from the inventory.

### M5-2 (minor test-strength gap): forwarded-query preservation does not prove "nothing added or removed"

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/AuthorizationContractTests.cs`,
`ValidAuthorizationRedirectsExactlyToUpstreamPreservingEveryAcceptedParameter`.

**Problem.** M5 requires tests proving scope "is never silently added or
removed". The valid-forwarding test asserts each expected key/value
individually but never asserts the **exact set** of forwarded parameters, and
no test proves that a request *without* `scope` is forwarded *without* `scope`.
An implementation that appended an extra parameter (or injected a default
scope) would still pass today's assertions.

**Guidance.**

1. In the preservation test, assert the forwarded query's key set equals
   exactly the input key set (e.g. compare `forwarded.Keys` ordered against the
   expected list).
2. Add a case (or a second assertion in an existing valid-request test, such as
   `ValidAuthorizationForwardsUnicodeAndEncodedValuesUnchanged`) proving a
   scope-less request redirects with no `scope` key present.
3. Result that demonstrates resolution: a hypothetical change that adds,
   defaults, or drops any parameter fails the contract suite.
