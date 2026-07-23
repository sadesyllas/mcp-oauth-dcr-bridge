using McpOAuthDcrBridge;
using System.Globalization;

namespace McpOAuthDcrBridge.ContractTests;

internal static class BridgeContractHost
{
    public static WebApplication Create(int permitLimit = 100, Action<List<string>>? configure = null)
    {
        var arguments = new List<string>
        {
            "--urls", "http://127.0.0.1:0",
            "--Bridge:ExternalBaseUrl", "https://bridge.example.test",
            "--Bridge:Upstream:AuthorizationEndpoint", "https://login.example.test/authorize",
            "--Bridge:Upstream:TokenEndpoint", "https://login.example.test/token",
            "--Bridge:Upstream:McpUrl", "https://mcp.example.test/streamable",
            "--Bridge:Upstream:ClientId", "fictional-client",
            "--Bridge:Upstream:ClientAuthentication:Method", "none",
            "--Bridge:AllowedRedirectUris:0", "https://client.example.test/callback",
            "--Bridge:Limits:RateLimitPermitLimit", permitLimit.ToString(CultureInfo.InvariantCulture),
        };
        configure?.Invoke(arguments);
        return BridgeApplication.Build([.. arguments]);
    }
}
