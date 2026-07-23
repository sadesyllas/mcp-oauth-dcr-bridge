using Microsoft.Extensions.Configuration;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

internal static class ValidBridgeConfiguration
{
    public static IConfiguration Create(Action<Dictionary<string, string?>>? configure = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Bridge:ExternalBaseUrl"] = "https://bridge.example.test/base",
            ["Bridge:Upstream:AuthorizationEndpoint"] = "https://login.example.test/authorize",
            ["Bridge:Upstream:TokenEndpoint"] = "https://login.example.test/token",
            ["Bridge:Upstream:McpUrl"] = "https://mcp.example.test/streamable",
            ["Bridge:Upstream:ClientId"] = "fictional-client",
            ["Bridge:Upstream:ClientAuthentication:Method"] = "none",
            ["Bridge:AllowedRedirectUris:0"] = "https://client.example.test/callback",
        };
        configure?.Invoke(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
