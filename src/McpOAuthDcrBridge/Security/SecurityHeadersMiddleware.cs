using System.Collections.Immutable;

namespace McpOAuthDcrBridge.Security;

/// <summary>
/// Applies bridge-wide security response headers: <c>X-Content-Type-Options: nosniff</c> on every
/// response, and cache suppression on the bounded set of OAuth-sensitive endpoints per RFC 6749 §5.1,
/// which requires responses containing tokens or credentials to never be cached.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private static readonly ImmutableHashSet<string> NoStorePaths = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "/register", "/authorize", "/token");

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware with the next delegate in the pipeline.</summary>
    /// <param name="next">The next request delegate.</param>
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Registers the bounded response headers and invokes the rest of the pipeline.</summary>
    /// <param name="context">The current request context.</param>
    /// <returns>A task that completes once the pipeline has finished handling the request.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var suppressCaching = NoStorePaths.Contains(context.Request.Path.Value ?? string.Empty);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (suppressCaching)
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });
        return _next(context);
    }
}
