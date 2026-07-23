using McpOAuthDcrBridge;
using System.Text.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class DiscoveryContractTests
{
    private static readonly string[] ExpectedScopes = ["scope-a", "scope-b"];
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
    [InlineData("Bearer token with-space")]
    [InlineData("Bearer token, Bearer another")]
    [InlineData("Bearer ==")]
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

    [Theory]
    [InlineData("application/json;q=0")]
    [InlineData("text/html, application/json;q=0")]
    [InlineData("application/json;q=0, */*;q=1")]
    [InlineData("application/*;q=0, */*;q=1")]
    public async Task MetadataHonorsExplicitJsonExclusion(string accept)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-authorization-server");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotAcceptable, response.StatusCode);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("Application/Json")]
    [InlineData("application/json;q=0.1, text/html;q=1")]
    [InlineData("*/*;q=0.5")]
    [InlineData("application/json;q=1, */*;q=0")]
    [InlineData("application/*;q=0.1, */*;q=1")]
    public async Task MetadataAcceptsPositiveJsonCompatibleRanges(string accept)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
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

    [Fact]
    public async Task ProtectedResourceMetadataEmitsConfiguredScopesExactly()
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments.Add("--Bridge:AllowedScopes:0");
            arguments.Add("scope-a");
            arguments.Add("--Bridge:AllowedScopes:1");
            arguments.Add("scope-b");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ExpectedScopes, document.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal("header", document.RootElement.GetProperty("bearer_methods_supported")[0].GetString());
        await application.StopAsync();
    }

    [Fact]
    public async Task DiscoveryRejectsUnsupportedMethodsAndKeepsJsonContentType()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var methodResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/.well-known/oauth-authorization-server"));
        using var jsonResponse = await client.GetAsync("/.well-known/oauth-protected-resource");

        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, methodResponse.StatusCode);
        Assert.Equal("application/json", jsonResponse.Content.Headers.ContentType!.MediaType);
        await application.StopAsync();
    }
}
