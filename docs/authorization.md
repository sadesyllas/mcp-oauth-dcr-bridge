# Authorization forwarding

`GET /authorize` is a stateless redirect endpoint. It never calls the upstream
authorization server itself; it validates the inbound request and, if
accepted, issues an HTTP redirect that sends the downstream client's browser
directly to the fixed configured upstream authorization endpoint.

## Validation

The bridge accepts only:

- the one configured client ID (`client_id`);
- an exact allowed redirect URI (`redirect_uri`), matched byte-for-byte against
  the configured allowlist, never by pattern or host match;
- `response_type=code`;
- a nonempty `code_challenge` with `code_challenge_method=S256`; `plain` and any
  other method are rejected;
- a scope within the optional configured allowlist, forwarded unchanged and
  never rewritten.

Any of `client_id`, `redirect_uri`, `response_type`, `code_challenge`,
`code_challenge_method`, `scope`, and `state` appearing more than once in the
query string is rejected outright, since a duplicated security-relevant
parameter is inherently ambiguous input.

## Failure behavior and the open-redirect boundary

`client_id` and `redirect_uri` are validated *before* any other check, and only
once both are confirmed to be the one configured pair does the bridge treat the
redirect URI as trustworthy. Failures at that stage return a bounded JSON error
directly, with no redirect at all, so the endpoint can never be used as an open
redirect. Every later failure (unsupported response type, PKCE downgrade,
disallowed scope) redirects to that same trusted callback with a standard
`error`/`error_description` (and `state`, if supplied) query, matching ordinary
OAuth authorization-server behavior.

## Forwarding

Every accepted standard and extension query parameter — including `state`,
`prompt`, hints, and RFC 8707 `resource` — is forwarded to the upstream
authorization endpoint with its original decoded value and multiplicity
preserved exactly. The bridge never adds, removes, maps, or normalizes a
parameter. The upstream authorization server then redirects the browser
directly to the downstream client's callback; the bridge does not see or relay
the resulting authorization code.

The redirect destination is always the one configured upstream authorization
endpoint. No inbound host, forwarding header, or query value can change it.

`/authorize` is rate limited using the same configurable bounded policy family
as other bridge endpoints, is never retried (the bridge performs no outbound
network call in this flow), and never logs the request query string or any
value within it.

See [the security model](security.md) for the open-redirect, PKCE-downgrade,
and confused-deputy threat model for this endpoint.
