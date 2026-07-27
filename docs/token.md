# Token and refresh forwarding

`POST /token` forwards authorization-code and refresh-token requests to the one
configured upstream token endpoint. The bridge never persists, inspects, or
independently refreshes a code, verifier, or refresh token; it validates the
request shape, forwards the form unchanged, and relays the upstream response.

## Accepted requests

- Content type must be exactly `application/x-www-form-urlencoded`, within the
  configured `TokenRequestBodyBytes` limit (16 KiB by default).
- `grant_type` is `authorization_code` or `refresh_token`; any other value, or a
  missing value, is rejected.
- `client_id` must equal the one configured fixed client ID.
- `authorization_code` requests require an exact allowed `redirect_uri` and a
  nonempty `code_verifier`; both are forwarded unchanged. The bridge does not
  perform the PKCE binding check itself — the upstream authorization server
  remains the authoritative verifier.
- `refresh_token` requests require a nonempty `refresh_token`, forwarded once
  and never read, stored, or reused by the bridge.
- Any of `grant_type`, `client_id`, `code`, `code_verifier`, `redirect_uri`,
  `refresh_token`, or `scope` occurring more than once in the form fails the
  request closed, since a duplicated security-relevant field is inherently
  ambiguous input.
- A downstream `Authorization` header, or a `client_secret`, `client_assertion`,
  or `client_assertion_type` form field, is rejected outright: only the bridge
  may add upstream client credentials, never the downstream caller.
- `scope` and every other accepted form field are forwarded with their exact
  decoded value and multiplicity; the bridge adds nothing except the configured
  upstream client authentication.

## Upstream client authentication

The configured `Bridge:Upstream:ClientAuthentication:Method` adds exactly one
credential to the *outbound* request only:

- `none` — no credential is added.
- `client_secret_post` — the configured secret is appended as a `client_secret`
  form field.
- `client_secret_basic` — the configured client ID and secret are sent as an
  HTTP Basic `Authorization` header.
- `private_key_jwt` — a fresh RFC 7523 client assertion is signed by the
  configured certificate's private key and added as `client_assertion` and
  `client_assertion_type` form fields. See
  [certificate-backed authentication](configuration.md#certificate-backed-private_key_jwt)
  for the supported certificate format and validation rules.

The credential is generated fresh for the outbound request and is never
returned downstream, logged, or exposed through diagnostics. Rotating a secret
or certificate requires restarting or redeploying with updated configuration;
the bridge holds no mutable credential state to invalidate.

## Response relay and failure behavior

The upstream status code, JSON body (token success or OAuth error), content
type, and safe headers are relayed unchanged; hop-by-hop headers (for example
`Connection` and `Transfer-Encoding`) are never forwarded in either direction.
Token requests are never automatically retried. A connection failure to the
upstream token endpoint maps to `502 Bad Gateway`; exceeding the configured
`OAuthTimeoutSeconds` maps to `504 Gateway Timeout`. Client cancellation
immediately cancels the outbound request.

See [the security model](security.md) for the credential-smuggling and
duplicated-parameter threat model for this endpoint.
