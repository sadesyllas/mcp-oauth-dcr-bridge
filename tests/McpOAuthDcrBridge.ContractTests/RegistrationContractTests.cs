using System.Net.Http.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class RegistrationContractTests
{
    [Fact]
    public async Task RegistrationIsRateLimited()
    {
        await using var application = BridgeContractHost.Create(permitLimit: 1);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var request = new { redirect_uris = new[] { "https://client.example.test/callback" } };
        using var first = await client.PostAsJsonAsync("/register", request);
        using var second = await client.PostAsJsonAsync("/register", request);

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, second.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task ValidRegistrationIsDeterministicAndPublic()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var request = new { redirect_uris = new[] { "https://client.example.test/callback" }, response_types = new[] { "code" }, grant_types = new[] { "authorization_code", "refresh_token" }, token_endpoint_auth_method = "none", scope = "mcp.read" };
        using var first = await client.PostAsJsonAsync("/register", request);
        using var second = await client.PostAsJsonAsync("/register", request);

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        var response = await first.Content.ReadAsStringAsync();
        Assert.Contains("fictional-client", response, StringComparison.Ordinal);
        Assert.Contains("\"token_endpoint_auth_method\":\"none\"", response, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", response, StringComparison.Ordinal);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("https://client.example.test/other")]
    [InlineData("https://client.example.test/callback/")]
    public async Task InvalidRedirectAndSmuggledCredentialsAreRejected(string redirectUri)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsJsonAsync("/register", new { redirect_uris = new[] { redirectUri }, client_secret = "canary-secret" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("canary-secret", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await application.StopAsync();
    }
}
