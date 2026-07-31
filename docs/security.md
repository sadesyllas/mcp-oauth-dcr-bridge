# Security model

The bridge is a stateless compatibility facade, not an identity provider. Its
fixed startup configuration prevents callers from selecting OAuth or MCP
destinations. Exact configured redirect strings prevent callback substitution,
and the downstream client remains public: the bridge never returns a client
secret.

## Dynamic client registration threats

Malicious DCR metadata can attempt redirect manipulation, credential smuggling,
software-statement confusion, replay, or denial of service. The bridge accepts
only exact configured redirect URIs; only `code`, `authorization_code`,
`refresh_token`, and `none` client authentication; and rejects client secrets,
JWK metadata, and software metadata. Registration has bounded JSON size and a
fixed-window limit. It stores no registration, token, or user state, so a
replayed valid request produces the same fixed public-client response.

Residual risk is bounded by the configured callback allowlist and upstream OAuth
client registration. Operators must protect configuration and rate-limit
capacity, and must not treat bridge acceptance as authorization to access an
upstream resource.

## Authorization forwarding threats

`GET /authorize` can be attacked through open redirect, client substitution,
authorization-server mix-up, PKCE downgrade, and duplicated or conflicting
security parameters. The bridge validates `client_id` and `redirect_uri` first
against exact configured values before treating a callback as trustworthy; only
then are later failures (unsupported response type, non-`S256` challenge
method, missing challenge, disallowed scope) reported by redirecting to that
already-validated callback. Any occurrence of a duplicated `client_id`,
`redirect_uri`, `response_type`, `code_challenge`, `code_challenge_method`,
`scope`, or `state` parameter fails closed without a redirect, since it is
inherently ambiguous input a downstream near-match or conflicting value could
otherwise exploit. `S256`-only PKCE prevents challenge downgrade to `plain`,
and the redirect destination is always the one fixed configured upstream
authorization endpoint, never selectable by `Host`, forwarding headers, or
query input, which forecloses use of the endpoint as an open or SSRF-capable
redirect.

The bridge does not receive the upstream authorization code: the upstream
authorization server redirects the browser directly to the downstream client's
callback. This direct-callback design assumes the upstream authorization
response does not carry an RFC 9207 `iss` value conflicting with the bridge
issuer; this assumption is an explicit external interoperability test gate
rather than a control enforced in code.

## Token and refresh forwarding threats

`POST /token` can be attacked through client-ID substitution, redirect
mismatch, PKCE-verifier omission, duplicated or conflicting security
parameters, and downstream credential smuggling (a supplied `client_secret`,
`client_assertion`, `client_assertion_type`, or `Authorization` header). The
bridge rejects all of these before any outbound call: `client_id` must equal
the fixed configured value, `redirect_uri` must exactly match the configured
allowlist, `code_verifier` and `refresh_token` must be nonempty, and any
smuggled credential field or header fails the request closed without ever
reaching the upstream endpoint or appearing in the bounded error response.

The configured upstream client secret or certificate exists only inside the
bridge process; it is added to the outbound request fresh on every call and is
never returned downstream, logged, or exposed through diagnostics or health
output. A refresh token is forwarded exactly once and is never read, stored,
cached, or reused by the bridge, which eliminates bridge-side replay as a
threat surface for that credential. Because the bridge has no outbound call in
`/authorize` but does have one in `/token`, this is also the first point where
an unreachable or slow upstream must fail safely: connection failures map to a
bounded `502`, timeouts to a bounded `504`, and neither response includes
upstream detail or secrets. Token requests are never automatically retried, so
an upstream failure cannot be amplified into duplicate authorization-code
redemption or refresh-token use.

## Certificate-backed `private_key_jwt` threats

A weak, expired, or misused signing certificate could let an attacker forge or
replay a client assertion, or the bridge could leak the private key through a
diagnostic surface. `PrivateKeyJwtCertificateLoader` fails startup for a
missing, corrupted, or wrong-password PKCS#12 file, a certificate without a
private key, an expired or not-yet-valid certificate, a key-usage extension
that excludes digital signatures, and an unsupported key algorithm — only RSA
and P-256 ECDSA are accepted. The private key is loaded into process memory
only (`X509KeyStorageFlags.EphemeralKeySet`, never a machine or user
certificate store) and the loaded certificate is excluded from every JSON,
health, and diagnostic representation of the configuration.

Each token or refresh request generates a fresh assertion: a cryptographically
random JWT ID (`RandomNumberGenerator`, not a predictable counter), the fixed
client ID as both issuer and subject, the exact configured token endpoint as
audience, and a short configured lifetime, all signed fresh from a new key
handle so concurrent requests cannot interfere with or replay each other's
assertion. Because a fresh assertion is minted per request and audience-bound
to the one configured token endpoint, a captured assertion cannot be replayed
against a different endpoint and has a short window even if intercepted.
Certificate rotation and rollback are both a file replacement plus a restart
or redeployment; the bridge caches no key material that would need explicit
invalidation.

## MCP reverse proxy threats

An MCP proxy is a natural SSRF/open-proxy target: a malicious request might try
to redirect the outbound call to an arbitrary host via an absolute-form
target, a spoofed `Host`/forwarding header, path traversal, or an encoded
path, or an attacker-controlled upstream might try to redirect the bridge or
leak upstream identity through a bearer challenge. YARP is configured with
exactly one route (matching only the literal `/mcp` path) and one cluster whose
destination is always the configured upstream origin with a path fixed by a
`PathSet` transform; none of the above inputs can change scheme, host, port, or
path, and a near-miss path never matches the route at all. Redirect responses
from the upstream are relayed to the caller rather than followed by the
bridge, so a malicious `3xx` cannot pivot the bridge itself into an unintended
destination.

The bearer token is opaque to the bridge in both directions: it is never
inspected, decoded, or logged, only forwarded byte-for-byte, which keeps token
validation exclusively the upstream resource server's responsibility. An
upstream `401` challenge is rewritten to drop any upstream-identifying
parameter (for example `realm`) and replace it with the bridge's own
`resource_metadata`, so a compromised or misconfigured upstream cannot use its
challenge to redirect a client's re-authentication toward a third party.
Configured static headers are applied only on the outbound leg, replace
same-named downstream values so a caller cannot override them, and are
rejected at both startup and forwarding time if they fall in the shared
forbidden-header set — closing header-injection and confused-deputy attempts
through that surface. MCP requests are never automatically retried, since MCP
tool calls can have side effects that must not be duplicated by the bridge.

## Request size, header, and request-smuggling limits

Oversized or malformed input is a denial-of-service and request-smuggling
surface. `POST /register` and `POST /token` bound the request body to
`Bridge:Limits:DcrRequestBodyBytes`/`TokenRequestBodyBytes` before any parsing
occurs, and reject the request without buffering more than that bound; an
oversized body fails with the same bounded JSON error as any other invalid
request rather than a distinct signal an attacker could use to probe the
limit. Configured upstream header names and values are validated against a
closed character set (`HttpFieldName`/`HttpFieldValue`) at startup and again
at forward time, which forecloses header-injection and request-smuggling
attempts through a configured value — an operator-supplied header can never
carry a raw CR/LF or other field-terminating byte. Every parsing boundary that
accepts caller-controlled text (URIs, query strings, form bodies, JSON
bodies, and the `Bearer` challenge parameter grammar) is covered by
deterministic-seed fuzz tests that assert the boundary always fails closed
with a bounded error rather than throwing an unhandled exception.

## Rate limiting and denial of service

`POST /register`, `GET /authorize`, and `POST /token` each have an
independently configurable fixed-window rate limit
(`Bridge:Limits:{Dcr,Authorize,Token}RateLimit{PermitLimit,WindowSeconds}`,
falling back to a shared default when unset — see
[configuration](configuration.md)). Independence matters because the three
endpoints have different legitimate traffic shapes and different abuse
profiles: a client-guessing attack against `/token` should not force the same
ceiling onto routine `/register` calls from a different integration, and vice
versa. A caller that exceeds its endpoint's limit receives a bounded `429`
with no upstream call made and no state retained. Residual risk: the limiter
partitions by endpoint, not by caller identity or source address, so a
distributed client population can still consume a shared budget; operators
who need per-client fairness should rate-limit at the ingress in front of the
bridge as well. The limiter's counters are also per-instance in-memory state,
never shared or replicated: this is deliberate, since the bridge is otherwise
fully stateless and a distributed limiter would be the one piece of
cross-instance state the whole design avoids, but it means the effective
limit scales with replica count under horizontal scaling. Operators running
more than one replica should size each endpoint's limit accordingly, or
enforce the aggregate ceiling at the ingress instead.

## HTTP response hardening

Every bridge response carries `X-Content-Type-Options: nosniff`, so a
browser-based caller can never be induced to sniff a bridge response as
executable content regardless of its declared content type. `/register`,
`/authorize`, and `/token` additionally carry `Cache-Control: no-store` and
`Pragma: no-cache` on every outcome, including validation failures, per RFC
6749 §5.1's requirement that responses carrying tokens or credentials never be
cached by a shared or browser cache. Discovery metadata is deliberately
excluded from that no-store set — it carries no credential and is served with
`Cache-Control: public, max-age=300` (see [discovery](discovery.md)) — and
health checks are unaffected, since neither surface can leak anything
cache-sensitive.

## Bounded errors, safe logging, and configuration diagnostics

Every externally visible error — JSON OAuth errors, redirect-carried errors,
and unhandled-exception responses — is a fixed, non-parameterized string plus
an RFC 6749 error code; none ever echoes caller input, an upstream response
body, or exception detail, which forecloses both information disclosure and
reflected-content attacks through the error surface itself. Every rejection
also increments a bounded `bridge.validation.rejections` counter keyed by
route and RFC 6749 error code, giving operators rejection-rate visibility
without ever recording the request content that caused it. Configuration
diagnostics (JSON serialization, health checks, logs, and telemetry) reveal
only that a secret-bearing setting is configured and which mode is active —
never the secret, certificate, or header value itself — via a closed
serialization boundary (`BridgeOptionsJsonConverter`) rather than an
opt-in redaction list a future field could bypass by omission.

## Dependency and container vulnerability management

`Directory.Build.props` enables NuGet's built-in audit
(`NuGetAudit=true`, `NuGetAuditMode=all`, `RestoreAuditProperties=all`,
`NuGetAuditLevel=high`) so any restore surfaces a warning for every direct and
transitive package with a known advisory at high severity or above; because
`TreatWarningsAsErrors=true`, an unresolved high or critical advisory fails
the build rather than merely logging a warning a reviewer could miss. As of
this review, `dotnet restore` and `dotnet list package --vulnerable
--include-transitive` report zero vulnerable packages across every project.
`dotnet list package --deprecated` flags `xunit` 2.9.3 (used only by the three
test projects, never shipped) as legacy in favor of `xunit.v3`; this is a
tracked maintenance item, not a vulnerability, and does not block this
milestone. Container image vulnerability scanning covers the OS and runtime
layers the NuGet audit above cannot see; see
[image vulnerability scanning](deployment.md#image-vulnerability-scanning)
for the scan commands, when they run, and the same unresolved-high/critical
release gate applied to the .NET dependency audit. As of this review, a
Trivy scan (`--severity HIGH,CRITICAL --exit-code 1 --ignore-unfixed`) of the
built image reports zero unresolved high or critical findings.
