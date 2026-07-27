using System.Collections.Immutable;

namespace McpOAuthDcrBridge.OAuth;

/// <summary>
/// Identifies the HTTP headers that are connection-specific and must never be copied when relaying a
/// response from one HTTP connection onto another, per RFC 9110 section 7.6.1.
/// </summary>
public static class HopByHopHeaders
{
    private static readonly ImmutableHashSet<string> Names = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length");

    /// <summary>Determines whether a header name is hop-by-hop and must not be relayed unchanged.</summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> when the header is connection-specific.</returns>
    public static bool IsHopByHop(string name) => Names.Contains(name);
}
