namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Centralizes telemetry redaction decisions so sensitive data cannot enter diagnostics.
/// </summary>
public static class TelemetryRedactor
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Api-Key",
    };

    /// <summary>Gets the fixed replacement used wherever sensitive data would otherwise be emitted.</summary>
    public const string RedactedValue = "[REDACTED]";

    /// <summary>Returns a safe representation of any externally supplied or configured secret value.</summary>
    /// <param name="headerName">The header name that determines whether its value is sensitive.</param>
    /// <param name="value">The untrusted header value.</param>
    /// <returns>The redaction marker; telemetry never emits header values.</returns>
    public static string HeaderValue(string headerName, string value) => RedactedValue;

    /// <summary>Returns a safe configuration error description that excludes the configured value.</summary>
    /// <param name="configurationKey">The configuration key associated with an error.</param>
    /// <returns>A bounded non-secret diagnostic description.</returns>
    public static string ConfigurationError(string configurationKey) => $"Configuration validation failed for {configurationKey}.";

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
