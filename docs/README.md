# Documentation

Provider-neutral architecture, security, configuration, deployment,
operations, and testing documentation for the bridge, as required by
`SPEC.md`.

| Document | Covers |
|---|---|
| [architecture.md](architecture.md) | Components, trust boundaries, sequence diagrams |
| [security.md](security.md) | Threat model, credential handling, redaction contract |
| [configuration.md](configuration.md) | The `Bridge` configuration schema, defaults, and examples |
| [deployment.md](deployment.md) | OCI image, TLS/ingress, scaling, secret mounting |
| [operations.md](operations.md) | Telemetry, shutdown, rotation, incident response |
| [testing.md](testing.md) | Automated suites, performance methodology, interoperability checklist |
| [discovery.md](discovery.md), [registration.md](registration.md), [authorization.md](authorization.md), [token.md](token.md), [mcp-proxy.md](mcp-proxy.md) | Narrative behavior of each endpoint surface |
| [openapi.json](openapi.json) | Machine-readable endpoint documentation |
