# MCP OAuth DCR Bridge

MCP OAuth DCR Bridge is a provider-neutral ASP.NET Core compatibility facade for
placing one OAuth-protected remote Model Context Protocol (MCP) server behind an
MCP client that requires OAuth Dynamic Client Registration (DCR).

Each deployment has exactly one fixed upstream MCP server and one
pre-registered upstream OAuth client. The bridge supplies the client-facing OAuth
discovery, DCR, authorization, token, and MCP surfaces required by the project
specification while retaining those fixed trust boundaries.

## Transparent-token model

The bridge is not an identity provider, token store, or authorization decision
point. Users authenticate with the upstream authorization server; it issues the
access and refresh tokens. The downstream MCP client keeps those tokens, and the
upstream MCP server remains responsible for validating and authorizing bearer
tokens. The bridge never mints, persists, introspects, or transforms tokens.

## Non-goals

- Multiple upstream MCP servers in a deployment.
- Login or consent screens, identity federation, and user-session storage.
- Token storage, validation, exchange, or independently managed refresh.
- Provider-specific protocol branches or configuration.
- General-purpose proxying, MCP aggregation, and stdio transport.

## Repository layout

```text
McpOAuthDcrBridge.sln
src/McpOAuthDcrBridge/                   Deployable ASP.NET Core application
tests/McpOAuthDcrBridge.UnitTests/        Isolated validation and transformation tests
tests/McpOAuthDcrBridge.IntegrationTests/ In-process application tests
tests/McpOAuthDcrBridge.ContractTests/    External HTTP and protocol contract tests
docs/                                     Architecture and operator documentation
```

## Quick start

Install the .NET 10 SDK (`global.json` pins the tested SDK feature band),
then provide a complete `Bridge` configuration — see
[`docs/configuration.md`](docs/configuration.md) for the schema,
secret-provider guidance, a fictional example, and the local-development
HTTP exception — and run:

```sh
dotnet run --project src/McpOAuthDcrBridge/McpOAuthDcrBridge.csproj
```

Use `Ctrl+C` to verify graceful shutdown; see
[`docs/operations.md`](docs/operations.md#graceful-shutdown) for the drain
behavior that triggers.

## Local development and engineering commands

Run all commands at the repository root:

```sh
dotnet restore McpOAuthDcrBridge.sln
dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore
dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore
dotnet test McpOAuthDcrBridge.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet list McpOAuthDcrBridge.sln package --vulnerable --include-transitive
```

The restore gate enables NuGet auditing and fails the build for high or critical
known vulnerabilities.

## Testing

`dotnet test McpOAuthDcrBridge.sln` is the single repository-level command
for the unit, integration, and contract suites — no real OAuth provider or
MCP server is ever required; every suite runs against fakes or in-process.
See [`docs/testing.md`](docs/testing.md) for what each suite proves, the
repeatable performance benchmark methodology and reference results, and the
manual external-interoperability acceptance checklist for real deployments.

## Container execution

```sh
docker build -t mcp-oauth-dcr-bridge:local .
docker run --rm -p 8080:8080 \
  -e "Bridge__ExternalBaseUrl=https://bridge.example.test" \
  -e "Bridge__Upstream__AuthorizationEndpoint=https://login.example.test/authorize" \
  -e "Bridge__Upstream__TokenEndpoint=https://login.example.test/token" \
  -e "Bridge__Upstream__McpUrl=https://mcp.example.test/streamable" \
  -e "Bridge__Upstream__ClientId=fictional-client" \
  -e "Bridge__Upstream__ClientAuthentication__Method=none" \
  -e "Bridge__AllowedRedirectUris__0=https://client.example.test/callback" \
  mcp-oauth-dcr-bridge:local
```

The image runs as a non-root user, listens on port `8080` over plain HTTP
(terminate TLS at an ingress in front of it), and accepts the identical
configuration contract as `dotnet run`. See
[`docs/deployment.md`](docs/deployment.md) for the full container contract,
scaling, and secret-mounting guidance, and run
`scripts/container-smoke-test.sh` for automated build/readiness/shutdown
smoke coverage.

## Documentation and project governance

[`SPEC.md`](SPEC.md) is the authoritative behavior and architecture contract.
[`MILESTONES.md`](MILESTONES.md) defines the required ordered implementation
sequence and quality gates. [`docs/`](docs/) contains the full architecture,
security, configuration, deployment, operations, and testing documentation,
plus [machine-readable endpoint documentation](docs/openapi.json).
