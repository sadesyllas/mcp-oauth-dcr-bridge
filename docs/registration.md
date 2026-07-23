# Dynamic client registration

`POST /register` is a deterministic RFC 7591 compatibility endpoint. It creates
no record, secret, registration access token, or management URL. Every valid
request returns the one configured client ID as a public client with
`token_endpoint_auth_method: none`.

The endpoint accepts JSON only and applies the configured DCR request-size and
rate limits. All supplied `redirect_uris` must be unique and exactly equal to
configured callback values. Only response type `code`, grants
`authorization_code` and `refresh_token`, and public token authentication are
accepted. If a scope allowlist is configured, requested scope tokens must be in
it; approved scope text is returned unchanged.

To prevent a confused-deputy or credential-smuggling path, the bridge rejects
client secrets, JWK metadata, and software metadata. Error responses are bounded
and never echo submitted metadata or secrets.
