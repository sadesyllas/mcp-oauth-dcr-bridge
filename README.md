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

## Development requirements

Install the .NET 10 SDK. `global.json` selects the tested SDK feature band.
No provider credentials are needed to build or run the M0 scaffold.

## Engineering commands

Run all commands at the repository root:

```sh
dotnet restore McpOAuthDcrBridge.sln
dotnet build McpOAuthDcrBridge.sln --configuration Release --no-restore
dotnet format McpOAuthDcrBridge.sln --verify-no-changes --no-restore
dotnet test McpOAuthDcrBridge.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet list McpOAuthDcrBridge.sln package --vulnerable --include-transitive
```

The restore gate enables NuGet auditing and fails the build for high or critical
known vulnerabilities. The test command is the single repository-level command
for all unit, integration, and contract suites.

M1 requires a complete `Bridge` configuration contract at startup. See
[`docs/configuration.md`](docs/configuration.md) for the schema, secret-provider
guidance, fictional example, and local-development HTTP exception. With a valid
configuration source, run the host locally with:

```sh
dotnet run --project src/McpOAuthDcrBridge/McpOAuthDcrBridge.csproj
```

Use `Ctrl+C` to verify graceful shutdown.

## Project governance

[`SPEC.md`](SPEC.md) is the authoritative behavior and architecture contract.
[`MILESTONES.md`](MILESTONES.md) defines the required ordered implementation
sequence and quality gates.
