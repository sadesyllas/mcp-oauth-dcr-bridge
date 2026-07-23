namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Defines the single authoritative set of static headers forbidden on upstream MCP requests.
/// </summary>
public static class ForbiddenUpstreamMcpHeaders
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Host", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade",
        "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Via",
        "Traceparent", "Tracestate", "Baggage", "X-Correlation-ID", "Mcp-Session-Id", "Last-Event-ID",
        "MCP-Protocol-Version", "Proxy-Authorization", "Proxy-Connection",
    };

    /// <summary>Determines whether a header name is forbidden for configured MCP upstream headers.</summary>
    /// <param name="headerName">The case-insensitive header name to evaluate.</param>
    /// <returns><see langword="true"/> when the name is application-controlled or unsafe.</returns>
    public static bool Contains(string headerName) => Names.Contains(headerName) || headerName.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);
}
