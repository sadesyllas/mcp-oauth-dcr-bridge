namespace McpOAuthDcrBridge.Discovery;

/// <summary>
/// Produces a discovery response with controlled JSON and cache headers.
/// </summary>
public sealed class DiscoveryResult : IResult
{
    private readonly int statusCode;
    private readonly object? body;
    private readonly string? cacheControl;

    /// <summary>Initializes a discovery response.</summary>
    /// <param name="statusCode">The HTTP status code to send.</param>
    /// <param name="body">The optional JSON document body.</param>
    /// <param name="cacheControl">The optional explicit cache policy.</param>
    public DiscoveryResult(int statusCode, object? body, string? cacheControl)
    {
        this.statusCode = statusCode;
        this.body = body;
        this.cacheControl = cacheControl;
    }

    /// <summary>Writes the controlled discovery response.</summary>
    /// <param name="httpContext">The HTTP context receiving the result.</param>
    /// <returns>A task representing JSON serialization completion.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        if (cacheControl is not null) httpContext.Response.Headers.CacheControl = cacheControl;
        if (body is not null)
        {
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(body);
        }
    }
}
