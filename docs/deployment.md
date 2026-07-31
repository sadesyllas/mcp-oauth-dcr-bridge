# Deployment

The bridge ships as a single OCI image built from the repository-root
`Dockerfile`, and as a directly runnable ASP.NET Core binary
(`dotnet McpOAuthDcrBridge.dll` or `dotnet run`). Both forms load the identical
`Bridge` configuration contract described in [configuration](configuration.md)
— nothing about the configuration schema, validation, or secret handling
changes when moving from local execution to a container.

## Building and running the image

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

`scripts/container-smoke-test.sh` runs an automated version of this same
check — build, readiness, non-root user, in-flight-request survival across
`SIGTERM`, and fail-closed behavior on missing configuration — and is the
required M11 container acceptance evidence; run it from the repository root
with Docker available.

## Image contents and identity

The image is a two-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` restores
and publishes the single deployable project, and only the published output is
copied into `mcr.microsoft.com/dotnet/aspnet:10.0`, the pinned ASP.NET Core
runtime image appropriate for a .NET 10 LTS deployment (see
[the technology choices in SPEC.md](../SPEC.md)). No SDK, source, or test
project ever reaches the runtime layer. The process runs as that base image's
built-in non-root `app` user; the bridge never requires elevated privileges,
writes to its own image layers, or persists any file — it is fully stateless,
so no writable volume is needed at all.

Operators who need a specific, immutable digest rather than a floating
`10.0` tag should pin `FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:...`
in a local fork of the Dockerfile, recording the digest alongside the
deployment's change record.

## Port and health probes

The container listens on port `8080` over plain HTTP
(`ASPNETCORE_HTTP_PORTS=8080`); it never terminates TLS itself. Point a
container orchestrator's liveness probe at `GET /health/live` (process
running, no outbound calls) and its readiness probe at `GET /health/ready`
(startup configuration already validated, no outbound calls); both return
`200 Healthy` and are documented further in
[operations](operations.md). Neither endpoint requires authentication or
exposes configuration detail.

## TLS and ingress

Production ingress requires HTTPS. Terminate TLS at a trusted ingress,
load balancer, or service mesh sidecar in front of the container, and forward
plain HTTP to port `8080`; the bridge derives every public URL it emits
(`/mcp`, `/register`, `/authorize`, `/token`, and the discovery documents)
from the configured `Bridge:ExternalBaseUrl`, never from inbound `Host` or
forwarding headers, so a misconfigured or spoofed forwarding header at the
ingress cannot change what the bridge advertises as its own identity (see
[the security model](security.md) for the full forwarding-spoofing threat
model). Configure the ingress to forward the client's real `X-Correlation-ID`
if present, or omit it and let the bridge generate one.

## Scaling

The bridge holds no cross-request state: no session, no token, no
registration record, and no cache. Any number of replicas can run behind a
load balancer with no session affinity or shared storage requirement. The one
exception worth sizing for is the per-endpoint rate limiter (see
[the security model](security.md#rate-limiting-and-denial-of-service)):
its counters are deliberately per-instance in-memory state, so the effective
`/register`, `/authorize`, and `/token` limits scale with replica count.
Size `Bridge:Limits:{Dcr,Authorize,Token}RateLimit{PermitLimit,WindowSeconds}`
for the target replica count, or enforce the aggregate ceiling at the
ingress instead.

## Graceful shutdown

A container orchestrator's `SIGTERM` (for example a Kubernetes pod
termination or `docker stop`) starts the same graceful drain documented in
[operations](operations.md#graceful-shutdown): the bridge stops accepting new
work and gives in-flight requests, including open MCP streams, up to
`Bridge:Limits:ShutdownDrainTimeoutSeconds` to finish. Configure the
orchestrator's termination grace period to be at least that drain timeout
plus a margin for container teardown, or in-flight work is killed rather than
drained.

## Secret and certificate mounting

The container reads secrets exactly the same way the binary does: from
environment variables, a mounted configuration file, or a secret-provider
integration wired into `IConfiguration` — never from a value baked into the
image. Mount a client secret as an environment variable
(`Bridge__Upstream__ClientAuthentication__ClientSecret`) sourced from the
orchestrator's secret store (a Kubernetes `Secret`, a cloud secret manager,
or equivalent), and mount a `private_key_jwt` certificate file as a read-only
volume, pointing `Bridge__Upstream__ClientAuthentication__CertificatePath` at
its in-container path. The certificate's private key is loaded into process
memory only (`EphemeralKeySet`) and is never written back to disk by the
bridge; rotating either kind of secret is replacing the environment value or
mounted file and redeploying, exactly as described in
[credential and certificate rotation](operations.md#credential-and-certificate-rotation).
