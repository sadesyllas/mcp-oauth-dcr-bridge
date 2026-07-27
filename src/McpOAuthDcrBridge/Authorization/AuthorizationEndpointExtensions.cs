using System.Collections.Immutable;
using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace McpOAuthDcrBridge.Authorization;

/// <summary>
/// Maps the stateless authorization-forwarding endpoint that redirects validated requests to the fixed upstream
/// authorization server while preserving accepted parameters unchanged.
/// </summary>
public static class AuthorizationEndpointExtensions
{
    private static readonly ImmutableHashSet<string> SingleValuedParameters = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "client_id", "redirect_uri", "response_type", "code_challenge", "code_challenge_method", "scope", "state");

    /// <summary>Maps the bridge's authorization-forwarding endpoint.</summary>
    /// <param name="application">The application endpoint route builder.</param>
    /// <param name="options">The validated bridge configuration.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication MapAuthorizationEndpoint(this WebApplication application, BridgeOptions options)
    {
        application.MapGet("/authorize", (HttpRequest request) => Authorize(request, options)).RequireRateLimiting("authorize");
        return application;
    }

    private static IResult Authorize(HttpRequest request, BridgeOptions options)
    {
        var query = request.Query;

        // Any duplicated occurrence of a parameter the bridge inspects is ambiguous input and is never
        // trustworthy enough to redirect toward, even to a configured callback.
        if (OAuthFormParameters.HasDuplicate(query, SingleValuedParameters))
        {
            return OAuthErrorResult.Json("invalid_request", "an authorization parameter was duplicated");
        }

        // The client and callback must both be exact configured values before any redirect is issued;
        // this is the sole gate that prevents the endpoint from becoming an open redirect.
        if (!OAuthFormParameters.TrySingleValue(query, "redirect_uri", out var redirectUri) || !options.AllowedRedirectUris.Contains(redirectUri))
        {
            return OAuthErrorResult.Json("invalid_request", "redirect_uri is not an allowed callback");
        }

        if (!OAuthFormParameters.TrySingleValue(query, "client_id", out var clientId) || clientId != options.ClientId)
        {
            return OAuthErrorResult.Json("invalid_request", "client_id does not match the configured client");
        }

        var state = OAuthFormParameters.TrySingleValue(query, "state", out var stateValue) ? stateValue : null;

        if (!OAuthFormParameters.TrySingleValue(query, "response_type", out var responseType) || responseType != "code")
        {
            return RedirectWithError(redirectUri, "unsupported_response_type", "only the authorization code response type is supported", state);
        }

        if (!OAuthFormParameters.TrySingleValue(query, "code_challenge_method", out var challengeMethod) || challengeMethod != "S256")
        {
            return RedirectWithError(redirectUri, "invalid_request", "code_challenge_method must be S256", state);
        }

        if (!OAuthFormParameters.TrySingleValue(query, "code_challenge", out var challenge) || challenge.Length == 0)
        {
            return RedirectWithError(redirectUri, "invalid_request", "code_challenge is required", state);
        }

        if (OAuthFormParameters.TrySingleValue(query, "scope", out var scope) && !OAuthScopePolicy.IsAllowed(scope, options.AllowedScopes))
        {
            return RedirectWithError(redirectUri, "invalid_scope", "the requested scope is not permitted", state);
        }

        return Results.Redirect(QueryHelpers.AddQueryString(options.UpstreamAuthorizationEndpoint.AbsoluteUri, query), permanent: false, preserveMethod: false);
    }

    private static IResult RedirectWithError(string redirectUri, string error, string description, string? state)
    {
        var parameters = new Dictionary<string, StringValues> { ["error"] = error, ["error_description"] = description };
        if (state is not null) parameters["state"] = state;
        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, parameters), permanent: false, preserveMethod: false);
    }
}
