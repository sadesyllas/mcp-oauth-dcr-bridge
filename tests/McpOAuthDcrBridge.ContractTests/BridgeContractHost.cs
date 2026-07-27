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

    /// <summary>Creates a host whose upstream token endpoint points at a local fake server over plain HTTP.</summary>
    public static WebApplication CreateWithUpstreamToken(string tokenEndpointUrl, int permitLimit = 100, Action<List<string>>? configure = null) =>
        Create(permitLimit, arguments => AllowLocalHttpUpstream(arguments, "https://login.example.test/token", tokenEndpointUrl, configure));

    /// <summary>Creates a host whose upstream MCP endpoint points at a local fake server over plain HTTP.</summary>
    public static WebApplication CreateWithUpstreamMcp(string mcpUrl, int permitLimit = 100, Action<List<string>>? configure = null) =>
        Create(permitLimit, arguments => AllowLocalHttpUpstream(arguments, "https://mcp.example.test/streamable", mcpUrl, configure));

    private static void AllowLocalHttpUpstream(List<string> arguments, string placeholder, string replacement, Action<List<string>>? configure)
    {
        arguments.Add("--environment");
        arguments.Add("Development");
        arguments.Add("--Bridge:AllowHttpForLocalDevelopment");
        arguments.Add("true");
        arguments[arguments.IndexOf(placeholder)] = replacement;
        configure?.Invoke(arguments);
    }
}
