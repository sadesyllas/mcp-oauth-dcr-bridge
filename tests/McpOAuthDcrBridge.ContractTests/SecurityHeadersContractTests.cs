using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class SecurityHeadersContractTests
{
    private const string Redirect = "https://client.example.test/callback";

    [Fact]
    public async Task EveryResponseCarriesNosniff()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        using var health = await client.GetAsync("/health/live");
        using var registration = await client.PostAsJsonAsync("/register", new { redirect_uris = new List<string> { Redirect } });
        using var authorize = await client.GetAsync($"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256");
        using var token = await client.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "canary" }));
        using var discovery = await client.GetAsync("/.well-known/oauth-authorization-server");
        using var notFound = await client.GetAsync("/does-not-exist");

        foreach (var response in new[] { health, registration, authorize, token, discovery, notFound })
        {
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        }
        await application.StopAsync();
    }

    [Fact]
    public async Task OAuthEndpointsAreNeverCached()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        using var registration = await client.PostAsJsonAsync("/register", new { redirect_uris = new List<string> { Redirect } });
        using var invalidRegistration = await client.PostAsJsonAsync("/register", new { redirect_uris = new List<string> { "https://attacker.example.test/callback" } });
        using var authorize = await client.GetAsync($"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256");
        using var rejectedAuthorize = await client.GetAsync($"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=unsupported&code_challenge=challenge&code_challenge_method=S256");
        using var token = await client.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "canary" }));

        foreach (var response in new[] { registration, invalidRegistration, authorize, rejectedAuthorize, token })
        {
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("no-cache", response.Headers.Pragma.ToString());
        }
        await application.StopAsync();
    }

    [Fact]
    public async Task DiscoveryKeepsItsOwnPublicCachingUnaffected()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        using var discovery = await client.GetAsync("/.well-known/oauth-authorization-server");

        Assert.Equal("public, max-age=300", discovery.Headers.CacheControl!.ToString());
        Assert.Empty(discovery.Headers.Pragma);
        await application.StopAsync();
    }

    [Fact]
    public async Task UnrelatedRoutesAreNotForcedNoStore()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);

        using var notFound = await client.GetAsync("/does-not-exist");

        Assert.Null(notFound.Headers.CacheControl);
        await application.StopAsync();
    }

    private static HttpClient NonRedirectingClient(WebApplication application) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(application.Urls.Single()) };
}
