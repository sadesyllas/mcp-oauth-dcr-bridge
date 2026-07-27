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
