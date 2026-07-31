# syntax=docker/dockerfile:1
#
# Multi-stage build for the MCP OAuth DCR Bridge. The build stage restores and
# publishes the single deployable project; the runtime stage carries only the
# published output on top of the pinned ASP.NET Core runtime image, running as
# the image's built-in non-root "app" user. See docs/deployment.md for the
# full deployment contract (port, health probes, shutdown, secret mounting).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props McpOAuthDcrBridge.sln ./
COPY src/McpOAuthDcrBridge/McpOAuthDcrBridge.csproj src/McpOAuthDcrBridge/
RUN dotnet restore src/McpOAuthDcrBridge/McpOAuthDcrBridge.csproj

COPY src/McpOAuthDcrBridge/ src/McpOAuthDcrBridge/
RUN dotnet publish src/McpOAuthDcrBridge/McpOAuthDcrBridge.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "McpOAuthDcrBridge.dll"]
