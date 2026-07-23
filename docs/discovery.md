# OAuth discovery

The bridge publishes canonical OAuth metadata at
`/.well-known/oauth-protected-resource` and
`/.well-known/oauth-authorization-server`. Both documents are derived only from
the validated `Bridge:ExternalBaseUrl` and never from `Host` or forwarding
headers.

The protected-resource document identifies the canonical `/mcp` resource, the
bridge issuer, configured scopes, and bearer-header use. The authorization
server document advertises only the bridge `/register`, `/authorize`, and
`/token` paths, response type `code`, grants `authorization_code` and
`refresh_token`, public client authentication `none`, and PKCE `S256`.

The documents use `Cache-Control: public, max-age=300`. A request to `/mcp`
without bearer authorization receives a `401` Bearer challenge whose
`resource_metadata` points back to the canonical protected-resource document.
