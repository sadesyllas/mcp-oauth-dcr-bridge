namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Centralizes telemetry redaction decisions so sensitive data cannot enter diagnostics.
/// </summary>
public static class TelemetryRedactor
{
    /// <summary>Maps an HTTP method to the bounded telemetry vocabulary.</summary>
    /// <param name="method">The inbound HTTP method.</param>
    /// <returns>A recognized method or <c>OTHER</c>, never caller-controlled text.</returns>
    public static string HttpMethod(string method) => method switch
    {
        "GET" => "GET",
        "POST" => "POST",
        "DELETE" => "DELETE",
        _ => "OTHER",
    };

    /// <summary>Maps an HTTP response status to a bounded result category.</summary>
    /// <param name="statusCode">The final HTTP response status.</param>
    /// <returns>The status class category used in logs, spans, and metrics.</returns>
    public static string ResultCategory(int statusCode) => $"{Math.Clamp(statusCode / 100, 1, 5)}xx";
}
