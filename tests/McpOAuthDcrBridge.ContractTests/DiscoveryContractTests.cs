using McpOAuthDcrBridge;
using System.Text.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class DiscoveryContractTests
{
    [Fact]
    public async Task DiscoveryAndChallengeUseOnlyCanonicalConfiguration()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var metadata = await client.GetAsync("/.well-known/oauth-authorization-server?secret=never-log");
        using var challenge = await client.GetAsync("/mcp");

        Assert.Equal(System.Net.HttpStatusCode.OK, metadata.StatusCode);
        Assert.Equal("public, max-age=300", metadata.Headers.CacheControl!.ToString());
        using var metadataJson = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync());
        Assert.Equal("https://bridge.example.test/", metadataJson.RootElement.GetProperty("issuer").GetString());
        Assert.Equal("https://bridge.example.test/register", metadataJson.RootElement.GetProperty("registration_endpoint").GetString());
        Assert.Equal("code", metadataJson.RootElement.GetProperty("response_types_supported")[0].GetString());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Equal("Bearer resource_metadata=\"https://bridge.example.test/.well-known/oauth-protected-resource\"", challenge.Headers.WwwAuthenticate.Single()!.ToString());
        await application.StopAsync();
    }

    [Theory]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    public async Task McpChallengesMissingAndMalformedBearerAuthorization(string authorization)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task MetadataRejectsUnsupportedAcceptAndIgnoresHostPoisoning()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var rejected = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource");
        rejected.Headers.Accept.ParseAdd("text/html");
        using var poisoned = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource");
        poisoned.Headers.Host = "attacker.example.test";
        poisoned.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.example.test");
        using var rejectedResponse = await client.SendAsync(rejected);
        using var poisonedResponse = await client.SendAsync(poisoned);

        Assert.Equal(System.Net.HttpStatusCode.NotAcceptable, rejectedResponse.StatusCode);
        Assert.Contains("https://bridge.example.test/mcp", await poisonedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await application.StopAsync();
    }
}
