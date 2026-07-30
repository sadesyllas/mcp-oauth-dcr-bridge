using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.Mcp;

/// <summary>
/// Returns the bridge's discovery challenge for an unauthenticated MCP request before any proxying is
/// attempted, so an absent credential is never forwarded anywhere. A request carrying a bearer
/// credential is passed through unchanged to the reverse proxy.
/// </summary>
public sealed class McpChallengeMiddleware
{
    private readonly RequestDelegate next;
    private readonly BridgeOptions options;

    /// <summary>Initializes a new MCP challenge middleware instance.</summary>
    /// <param name="next">The next request handler, typically endpoint routing to the reverse proxy.</param>
    /// <param name="options">The validated bridge configuration.</param>
    public McpChallengeMiddleware(RequestDelegate next, BridgeOptions options)
    {
        this.next = next;
        this.options = options;
    }

    /// <summary>Challenges an unauthenticated MCP request, otherwise forwards it unchanged.</summary>
    /// <param name="context">The inbound HTTP context.</param>
    /// <returns>A task that completes once the challenge or the remaining pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (IsMcpRequest(context.Request) && !HasBearerCredential(context.Request))
        {
            WriteChallenge(context, options);
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsMcpRequest(HttpRequest request) =>
        request.Path.Equals("/mcp", StringComparison.Ordinal) && request.Method is "GET" or "POST" or "DELETE";

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

    private static void WriteChallenge(HttpContext context, BridgeOptions options)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = BearerChallenge.Build(options);
    }
}
