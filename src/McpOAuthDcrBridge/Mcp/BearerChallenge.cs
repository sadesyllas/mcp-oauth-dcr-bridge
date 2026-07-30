using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.Mcp;

/// <summary>
/// Builds the one authoritative <c>WWW-Authenticate</c> Bearer challenge that binds MCP clients to the
/// bridge's protected-resource metadata, optionally preserving a bounded subset of an upstream
/// challenge's parameters.
/// </summary>
public static class BearerChallenge
{
    private static readonly string[] PreservedParameterNames = ["error", "error_description", "scope"];

    /// <summary>Builds a complete <c>Bearer</c> challenge header value identifying the bridge's protected-resource metadata.</summary>
    /// <param name="options">The validated bridge configuration.</param>
    /// <param name="preservedParameters">Optional upstream challenge parameters to carry through; only <c>error</c>, <c>error_description</c>, and <c>scope</c> are preserved.</param>
    /// <returns>A complete <c>WWW-Authenticate</c> header value using the <c>Bearer</c> scheme.</returns>
    public static string Build(BridgeOptions options, IReadOnlyDictionary<string, string>? preservedParameters = null)
    {
        var parameters = new List<string> { $"resource_metadata=\"{options.ProtectedResourceMetadataUri.AbsoluteUri}\"" };
        if (preservedParameters is not null)
        {
            foreach (var name in PreservedParameterNames)
            {
                if (preservedParameters.TryGetValue(name, out var value))
                {
                    parameters.Add($"{name}=\"{value}\"");
                }
            }
        }

        return $"Bearer {string.Join(", ", parameters)}";
    }
}
