namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

internal static class ValidBridgeCommandLine
{
    public static string[] Arguments => Create("none");

    public static string[] Create(string method, string? secret = null, string? certificatePath = null)
    {
        var arguments = new List<string>
        {
        "--urls", "http://127.0.0.1:0",
        "--Bridge:ExternalBaseUrl", "https://bridge.example.test",
        "--Bridge:Upstream:AuthorizationEndpoint", "https://login.example.test/authorize",
        "--Bridge:Upstream:TokenEndpoint", "https://login.example.test/token",
        "--Bridge:Upstream:McpUrl", "https://mcp.example.test/streamable",
        "--Bridge:Upstream:ClientId", "fictional-client",
        "--Bridge:Upstream:ClientAuthentication:Method", method,
        "--Bridge:AllowedRedirectUris:0", "https://client.example.test/callback",
        };
        if (secret is not null)
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:ClientSecret");
            arguments.Add(secret);
        }

        if (certificatePath is not null)
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:CertificatePath");
            arguments.Add(certificatePath);
        }

        return [.. arguments];
    }
}
