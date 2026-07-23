using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
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

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        var response = await first.Content.ReadAsStringAsync();
        using var responseJson = JsonDocument.Parse(response);
        Assert.Equal("fictional-client", responseJson.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("none", responseJson.RootElement.GetProperty("token_endpoint_auth_method").GetString());
        Assert.False(responseJson.RootElement.TryGetProperty("client_secret", out _));
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

    [Theory]
    [InlineData("{\"redirect_uris\":[\"https://client.example.test/callback\"],\"response_types\":[]}")]
    [InlineData("{\"redirect_uris\":[\"https://client.example.test/callback\"],\"grant_types\":[\"authorization_code\",\"authorization_code\"]}")]
    [InlineData("{\"redirect_uris\":[\"https://client.example.test/callback\"],\"scope\":\"mcp.read  mcp.write\"}")]
    [InlineData("{\"redirect_uris\":[\"https://client.example.test/callback\"],\"software_statement\":\"opaque\"}")]
    [InlineData("{\"redirect_uris\":[\"https://client.example.test/callback\"],\"redirect_uris\":[\"https://client.example.test/callback\"]}")]
    public async Task RegistrationRejectsAmbiguousOrUnsupportedMetadata(string json)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsync("/register", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task RegistrationIsStatelessAcrossHostRestart()
    {
        const string json = "{\"redirect_uris\":[\"https://client.example.test/callback\"]}";
        var first = await RegisterAfterStartAsync(json);
        var second = await RegisterAfterStartAsync(json);

        Assert.Equal(first, second);
    }

    private static async Task<string> RegisterAfterStartAsync(string json)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsync("/register", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        await application.StopAsync();
        return body;
    }
}
