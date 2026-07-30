# Configuration

The bridge reads its single deployment contract from the `Bridge` configuration
section. Values may come from JSON, environment variables, command-line
arguments, or an external secret provider. Do not commit client secrets or
certificate files. Environment variables replace `:` with `__`, for example
`Bridge__Upstream__ClientAuthentication__ClientSecret`.

All URLs are fixed at startup. In production they must be absolute HTTPS URLs
without credentials, query strings, or fragments. `AllowHttpForLocalDevelopment`
defaults to `false`; it permits HTTP only when the ASP.NET Core environment is
`Development` and every HTTP host is `localhost` or a loopback IP address.
Remote DNS/IP hosts, deceptive `localhost` suffixes, and URI user-info are
rejected, keeping the exception limited to local development dependencies.

```json
{
  "Bridge": {
    "ExternalBaseUrl": "https://bridge.example.test/",
    "Upstream": {
      "AuthorizationEndpoint": "https://login.example.test/oauth/authorize",
      "TokenEndpoint": "https://login.example.test/oauth/token",
      "McpUrl": "https://mcp.example.test/api/streamable",
      "ClientId": "fictional-mcp-client",
      "ClientAuthentication": { "Method": "client_secret_post" },
      "McpHeaders": [
        { "Name": "X-Deployment-Context", "Values": [ "fictional-production" ] }
      ]
    },
    "AllowedRedirectUris": [ "https://client.example.test/oauth/callback" ],
    "AllowedScopes": [ "mcp.read", "mcp.write" ]
  }
}
```

`ExternalBaseUrl` is the canonical public base. The bridge derives `/mcp`,
`/register`, `/authorize`, and `/token` from it and never uses inbound Host or
forwarded headers to change those public values. Every upstream destination and
the client ID are required. Callback URIs are literal absolute values, with no
fragments, patterns, wildcards, or duplicate entries. An empty `AllowedScopes`
list means no allowlist; otherwise every configured item is one exact scope token
without whitespace.

## Upstream client authentication

`Bridge:Upstream:ClientAuthentication:Method` is exactly one of:

- `none` — no secret or certificate setting is allowed.
- `client_secret_post` or `client_secret_basic` — requires `ClientSecret` from a
  secret provider; a certificate setting is not allowed.
- `private_key_jwt` — requires `CertificatePath` to a PKCS#12 (`.pfx`) file containing the private
  key; `CertificatePassword` is optional; a client secret is not allowed.

The bridge treats client secrets, certificate material, and static header values
as secrets. Startup errors name only the invalid configuration key and never
return values.

### Certificate-backed `private_key_jwt`

The configured PKCS#12 file must carry an RSA or P-256 ECDSA private key, be
within its validity window, and not disallow digital signatures through a
present key-usage extension. Startup fails if the file is missing, corrupted,
has an incorrect password, is expired or not yet valid, uses an unsupported key
algorithm, or lacks a private key. The private key is loaded into process
memory only — never a machine or user certificate store — and is never
exported, logged, serialized, or exposed through errors, health checks, or
telemetry. Each token or refresh request receives a freshly generated,
uniquely identified assertion; nothing about the key is cached across
signatures beyond the loaded certificate itself.

Rotate a certificate by replacing the mounted file and restarting or
redeploying; the bridge holds no cached copy to invalidate. Rolling back is the
same operation using the previous file.

## Static MCP headers and limits

`Bridge:Upstream:McpHeaders` is an ordered configuration list, though header order has no
semantic meaning. Each item has a unique case-insensitive `Name` and one or
more nonempty `Values`; the full value list is intentional and enables headers
that validly support multiple values. These values are applied only to upstream
MCP requests and replace downstream headers with the same name. Authorization,
transport, forwarding, tracing/correlation, proxy, and MCP-session/protocol
headers are rejected at startup, and the same forbidden-header set is checked
again when the bridge actually forwards a request. See
[the MCP reverse proxy](mcp-proxy.md) for how these headers are applied.

`Bridge:Limits` uses these safe defaults and bounds:

| Setting | Default | Allowed range |
|---|---:|---:|
| `DcrRequestBodyBytes` | 32 KiB | 1 KiB–1 MiB |
| `TokenRequestBodyBytes` | 16 KiB | 1 KiB–1 MiB |
| `OAuthTimeoutSeconds` | 30 s | 1–120 s |
| `McpActivityTimeoutSeconds` | 300 s | 1–3,600 s |
| `ShutdownDrainTimeoutSeconds` | 30 s | 1–300 s |
| `RateLimitPermitLimit` | 100 | 1–10,000 |
| `RateLimitWindowSeconds` | 60 s | 1–3,600 s |
| `DcrRateLimitPermitLimit` | `RateLimitPermitLimit` | 1–10,000 |
| `DcrRateLimitWindowSeconds` | `RateLimitWindowSeconds` | 1–3,600 s |
| `AuthorizeRateLimitPermitLimit` | `RateLimitPermitLimit` | 1–10,000 |
| `AuthorizeRateLimitWindowSeconds` | `RateLimitWindowSeconds` | 1–3,600 s |
| `TokenRateLimitPermitLimit` | `RateLimitPermitLimit` | 1–10,000 |
| `TokenRateLimitWindowSeconds` | `RateLimitWindowSeconds` | 1–3,600 s |
| `PrivateKeyJwtAssertionLifetimeSeconds` | 60 s | 10–600 s |

`RateLimitPermitLimit` and `RateLimitWindowSeconds` set the shared default fixed-window
rate limit; `/register`, `/authorize`, and `/token` each independently fall back to that
default. Set the endpoint-prefixed keys above to give one endpoint its own limit without
affecting the other two — for example, a tighter `TokenRateLimitPermitLimit` to slow
credential-guessing traffic while dynamic client registration keeps the shared default.

Configuration is resolved once during startup and injected as immutable options.
Changing a configuration provider after startup has no effect; deploy a new
instance to change a trust boundary or rotate configuration.
