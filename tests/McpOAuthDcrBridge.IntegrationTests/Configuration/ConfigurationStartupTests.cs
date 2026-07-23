using McpOAuthDcrBridge;
using McpOAuthDcrBridge.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed class ConfigurationStartupTests
{
    [Theory]
    [InlineData("none", null, null)]
    [InlineData("client_secret_post", "integration-secret", null)]
    [InlineData("client_secret_basic", "integration-secret", null)]
    [InlineData("private_key_jwt", null, "/run/secrets/integration.pfx")]
    public async Task ValidCredentialModesBuildAndStop(string method, string? secret, string? certificatePath)
    {
        await using var application = BridgeApplication.Build(ValidBridgeCommandLine.Create(method, secret, certificatePath));

        await application.StartAsync();
        await application.StopAsync();
    }

    [Fact]
    public void InvalidCredentialConfigurationFailsBeforeTheHostStarts()
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeApplication.Build(ValidBridgeCommandLine.Create("client_secret_basic")));

        Assert.Contains("ClientAuthentication", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("integration-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningRequestsRetainResolvedOptionsWhenAProviderReloads()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:ExternalBaseUrl"] = "https://bridge.example.test/",
            ["Bridge:Upstream:AuthorizationEndpoint"] = "https://login.example.test/authorize",
            ["Bridge:Upstream:TokenEndpoint"] = "https://login.example.test/token",
            ["Bridge:Upstream:McpUrl"] = "https://mcp.example.test/streamable",
            ["Bridge:Upstream:ClientId"] = "fixed-client",
            ["Bridge:Upstream:ClientAuthentication:Method"] = "client_secret_post",
            ["Bridge:Upstream:ClientAuthentication:ClientSecret"] = "original-secret",
            ["Bridge:AllowedRedirectUris:0"] = "https://client.example.test/callback",
        });
        await using var application = BridgeApplication.Build(["--urls", "http://127.0.0.1:0"], configuration);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        var requests = Enumerable.Range(0, 50).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-authorization-server");
            request.Headers.Host = "attacker.example.test";
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
            request.Headers.TryAddWithoutValidation("Forwarded", "host=attacker.example.test;proto=http");
            using var response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        });
        configuration["Bridge:ExternalBaseUrl"] = "https://attacker.example.test/";
        configuration["Bridge:Upstream:AuthorizationEndpoint"] = "https://attacker.example.test/authorize";
        configuration["Bridge:Upstream:ClientAuthentication:ClientSecret"] = "mutated-secret";
        var documents = await Task.WhenAll(requests);

        Assert.All(documents, document =>
        {
            Assert.Contains("https://bridge.example.test/", document, StringComparison.Ordinal);
            Assert.DoesNotContain("attacker.example.test", document, StringComparison.Ordinal);
            Assert.DoesNotContain("original-secret", document, StringComparison.Ordinal);
            Assert.DoesNotContain("mutated-secret", document, StringComparison.Ordinal);
        });
        await application.StopAsync();
    }
}
