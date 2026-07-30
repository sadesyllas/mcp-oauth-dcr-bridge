using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class RateLimitIndependenceContractTests
{
    private const string Redirect = "https://client.example.test/callback";

    [Fact]
    public async Task ExhaustingTheDcrLimitLeavesAuthorizeAndTokenUnaffected()
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:DcrRateLimitPermitLimit");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        Assert.Equal(HttpStatusCode.Created, await RegisterStatus(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await AuthorizeStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await TokenStatus(client));
        await application.StopAsync();
    }

    [Fact]
    public async Task ExhaustingTheAuthorizeLimitLeavesDcrAndTokenUnaffected()
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:AuthorizeRateLimitPermitLimit");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, await AuthorizeStatus(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AuthorizeStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await RegisterStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await TokenStatus(client));
        await application.StopAsync();
    }

    [Fact]
    public async Task ExhaustingTheTokenLimitLeavesDcrAndAuthorizeUnaffected()
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:TokenRateLimitPermitLimit");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, await TokenStatus(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, await TokenStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await RegisterStatus(client));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await AuthorizeStatus(client));
        await application.StopAsync();
    }

    private static async Task<HttpStatusCode> RegisterStatus(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/register", new { redirect_uris = new[] { Redirect } });
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> AuthorizeStatus(HttpClient client)
    {
        using var response = await client.GetAsync($"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256");
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> TokenStatus(HttpClient client)
    {
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "canary" }));
        return response.StatusCode;
    }

    private static HttpClient NonRedirectingClient(WebApplication application) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(application.Urls.Single()) };
}
