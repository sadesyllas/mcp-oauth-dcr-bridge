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
    public static WebApplication MapDiscoveryEndpoints(this WebApplication application, BridgeOptions options)
    {
        application.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) => MetadataResult(request, ProtectedResourceMetadata(options)));
        application.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) => MetadataResult(request, AuthorizationServerMetadata(options)));
        application.MapMethods("/mcp", ["GET", "POST", "DELETE"], (HttpRequest request) => HasBearerCredential(request) ? Results.NotFound() : ChallengeResult(options));
        return application;
    }

    private static bool AcceptsJson(HttpRequest request)
    {
        var acceptedMediaTypes = request.GetTypedHeaders().Accept;
        if (acceptedMediaTypes is not { Count: > 0 }) return true;

        var matchingRanges = acceptedMediaTypes
            .Select(range => new { Range = range, Specificity = JsonSpecificity(range.MediaType.Value) })
            .Where(candidate => candidate.Specificity >= 0)
            .ToArray();
        if (matchingRanges.Length == 0) return false;

        var mostSpecificMatch = matchingRanges.Max(candidate => candidate.Specificity);
        return matchingRanges
            .Where(candidate => candidate.Specificity == mostSpecificMatch)
            .Max(candidate => candidate.Range.Quality.GetValueOrDefault(1)) > 0;
    }

    private static int JsonSpecificity(string? mediaType) => mediaType switch
    {
        null => -1,
        _ when mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) => 2,
        _ when mediaType.Equals("application/*", StringComparison.OrdinalIgnoreCase) => 1,
        "*/*" => 0,
        _ => -1,
    };

    private static bool HasBearerCredential(HttpRequest request)
    {
        var values = request.Headers.Authorization;
        if (values.Count != 1 || values[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var token = value["Bearer ".Length..];
        if (token.Length == 0) return false;
        var paddingIndex = token.IndexOf('=');
        var core = paddingIndex < 0 ? token : token[..paddingIndex];
        var padding = paddingIndex < 0 ? string.Empty : token[paddingIndex..];
        return core.Length > 0 && core.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/') && padding.All(character => character == '=');
    }

    private static IResult MetadataResult(HttpRequest request, object metadata) =>
        HasBody(request) ? Results.StatusCode(StatusCodes.Status400BadRequest) :
        AcceptsJson(request) ? new DiscoveryResult(StatusCodes.Status200OK, metadata, "public, max-age=300") : Results.StatusCode(StatusCodes.Status406NotAcceptable);

    private static bool HasBody(HttpRequest request) => request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0;

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

    private static DiscoveryResult ChallengeResult(BridgeOptions options)
    {
        var metadataUri = new Uri(options.IssuerUri, ".well-known/oauth-protected-resource").AbsoluteUri;
        var encodedMetadataUri = Uri.EscapeDataString(metadataUri);
        return new DiscoveryResult(StatusCodes.Status401Unauthorized, null, null, $"Bearer resource_metadata=\"{encodedMetadataUri}\"");
    }
}
