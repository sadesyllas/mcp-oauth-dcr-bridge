using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.Discovery;

/// <summary>
/// Maps canonical OAuth discovery documents and the initial unauthenticated MCP challenge.
/// </summary>
public static class DiscoveryEndpointExtensions
{
    /// <summary>Maps discovery endpoints using only immutable validated bridge options.</summary>
    /// <param name="application">The application endpoint route builder.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication MapDiscoveryEndpoints(this WebApplication application)
    {
        var options = application.Services.GetRequiredService<BridgeOptions>();
        application.MapGet("/.well-known/oauth-protected-resource", () => MetadataResult(ProtectedResourceMetadata(options)));
        application.MapGet("/.well-known/oauth-authorization-server", () => MetadataResult(AuthorizationServerMetadata(options)));
        application.MapMethods("/mcp", ["GET", "POST", "DELETE"], (HttpRequest request) => request.Headers.Authorization.Count == 0 ? ChallengeResult(options) : Results.NotFound());
        return application;
    }

    private static DiscoveryResult MetadataResult(object metadata) => new(StatusCodes.Status200OK, metadata, "public, max-age=300");

    private static object ProtectedResourceMetadata(BridgeOptions options) => new
    {
        resource = options.McpResourceUri.AbsoluteUri,
        authorization_servers = new[] { options.IssuerUri.AbsoluteUri },
        scopes_supported = options.AllowedScopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray(),
        bearer_methods_supported = new[] { "header" },
    };

    private static object AuthorizationServerMetadata(BridgeOptions options) => new
    {
        issuer = options.IssuerUri.AbsoluteUri,
        registration_endpoint = options.RegistrationUri.AbsoluteUri,
        authorization_endpoint = options.AuthorizationUri.AbsoluteUri,
        token_endpoint = options.TokenUri.AbsoluteUri,
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        token_endpoint_auth_methods_supported = new[] { "none" },
        code_challenge_methods_supported = new[] { "S256" },
    };

    private static DiscoveryResult ChallengeResult(BridgeOptions options) => new(StatusCodes.Status401Unauthorized, null, null, $"Bearer resource_metadata=\"{options.IssuerUri}.well-known/oauth-protected-resource\"");
}
