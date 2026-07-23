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
