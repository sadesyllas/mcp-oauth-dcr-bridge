using McpOAuthDcrBridge;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class DiscoveryContractTests
{
    [Fact]
    public async Task DiscoveryAndChallengeUseOnlyCanonicalConfiguration()
    {
        await using var application = BridgeApplication.Build(["--urls", "http://127.0.0.1:0", "--Bridge:ExternalBaseUrl", "https://bridge.example.test", "--Bridge:Upstream:AuthorizationEndpoint", "https://login.example.test/authorize", "--Bridge:Upstream:TokenEndpoint", "https://login.example.test/token", "--Bridge:Upstream:McpUrl", "https://mcp.example.test/streamable", "--Bridge:Upstream:ClientId", "fictional-client", "--Bridge:Upstream:ClientAuthentication:Method", "none", "--Bridge:AllowedRedirectUris:0", "https://client.example.test/callback"]);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var metadata = await client.GetAsync("/.well-known/oauth-authorization-server?secret=never-log");
        using var challenge = await client.GetAsync("/mcp");

        Assert.Equal(System.Net.HttpStatusCode.OK, metadata.StatusCode);
        Assert.Equal("public, max-age=300", metadata.Headers.CacheControl!.ToString());
        Assert.Contains("https://bridge.example.test/register", await metadata.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Equal("Bearer resource_metadata=\"https://bridge.example.test/.well-known/oauth-protected-resource\"", challenge.Headers.WwwAuthenticate.Single()!.ToString());
        await application.StopAsync();
    }
}
