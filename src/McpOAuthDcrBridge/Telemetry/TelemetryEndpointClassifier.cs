namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Maps public bridge paths to the small bounded endpoint categories used in diagnostics.
/// </summary>
public static class TelemetryEndpointClassifier
{
    /// <summary>Returns the bounded category for a request path.</summary>
    /// <param name="path">The request path without its query string.</param>
    /// <returns>A stable endpoint category that never includes caller-controlled text.</returns>
    public static string Classify(PathString path) => path.Value switch
    {
        "/health/live" => "health_live",
        "/health/ready" => "health_ready",
        "/.well-known/oauth-protected-resource" => "protected_resource_metadata",
        "/.well-known/oauth-authorization-server" => "authorization_server_metadata",
        "/register" => "registration",
        "/authorize" => "authorization",
        "/token" => "token",
        "/mcp" => "mcp",
        _ => "other",
    };
}
