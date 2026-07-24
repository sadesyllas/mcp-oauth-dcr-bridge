namespace McpOAuthDcrBridge.OAuth;

/// <summary>
/// Produces the one bounded JSON OAuth error shape used before a redirect target is established as trustworthy.
/// </summary>
public static class OAuthErrorResult
{
    /// <summary>Returns a bounded, non-redirecting JSON OAuth error response.</summary>
    /// <param name="error">The RFC 6749 error code.</param>
    /// <param name="description">A bounded, non-sensitive error description that never echoes caller input.</param>
    /// <param name="statusCode">The HTTP status code; defaults to <see cref="StatusCodes.Status400BadRequest"/>.</param>
    /// <returns>A JSON result carrying the bounded error.</returns>
    public static IResult Json(string error, string description, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(new { error, error_description = description }, statusCode: statusCode);
}
