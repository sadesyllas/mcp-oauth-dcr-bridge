using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class AuthorizationContractTests
{
    private const string Redirect = "https://client.example.test/callback";
    private static readonly string[] FullParameterSet = ["client_id", "code_challenge", "code_challenge_method", "empty_extension", "login_hint", "prompt", "redirect_uri", "resource", "response_type", "scope", "state"];
    private static readonly string[] ScopelessParameterSet = ["client_id", "code_challenge", "code_challenge_method", "redirect_uri", "response_type", "state"];

    [Fact]
    public async Task ValidAuthorizationRedirectsExactlyToUpstreamPreservingEveryAcceptedParameter()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = "client_id=fictional-client" +
            "&redirect_uri=" + Uri.EscapeDataString(Redirect) +
            "&response_type=code" +
            "&code_challenge=abc123~-._" +
            "&code_challenge_method=S256" +
            "&state=xyz" +
            "&scope=mcp.read%20mcp.write" +
            "&resource=https%3A%2F%2Fmcp.example.test%2Fone" +
            "&resource=https%3A%2F%2Fmcp.example.test%2Ftwo" +
            "&prompt=consent" +
            "&login_hint=alice%40example.test" +
            "&empty_extension=";
        using var response = await client.GetAsync($"/authorize?{query}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https", location.Scheme);
        Assert.Equal("login.example.test", location.Host);
        Assert.Equal("/authorize", location.AbsolutePath);
        var forwarded = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(FullParameterSet, forwarded.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("fictional-client", forwarded["client_id"]);
        Assert.Equal(Redirect, forwarded["redirect_uri"]);
        Assert.Equal("code", forwarded["response_type"]);
        Assert.Equal("abc123~-._", forwarded["code_challenge"]);
        Assert.Equal("S256", forwarded["code_challenge_method"]);
        Assert.Equal("xyz", forwarded["state"]);
        Assert.Equal("mcp.read mcp.write", forwarded["scope"]);
        Assert.Equal(new StringValues(["https://mcp.example.test/one", "https://mcp.example.test/two"]), forwarded["resource"]);
        Assert.Equal("consent", forwarded["prompt"]);
        Assert.Equal("alice@example.test", forwarded["login_hint"]);
        Assert.Equal(string.Empty, forwarded["empty_extension"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task RequestWithoutScopeForwardsWithoutAddingOne()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256&state=xyz";
        using var response = await client.GetAsync($"/authorize?{query}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var forwarded = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(ScopelessParameterSet, forwarded.Keys.Order(StringComparer.Ordinal));
        Assert.False(forwarded.ContainsKey("scope"));
        await application.StopAsync();
    }

    [Fact]
    public async Task ValidAuthorizationForwardsUnicodeAndEncodedValuesUnchanged()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        const string state = "café-état-%2F";
        var query = "client_id=fictional-client" +
            $"&redirect_uri={Uri.EscapeDataString(Redirect)}" +
            "&response_type=code&code_challenge=challenge&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(state)}";
        using var response = await client.GetAsync($"/authorize?{query}");

        var forwarded = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(state, forwarded["state"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task AuthorizationRedirectDestinationIsAlwaysTheConfiguredUpstreamEndpoint()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = "client_id=fictional-client" +
            $"&redirect_uri={Uri.EscapeDataString(Redirect)}" +
            "&response_type=code&code_challenge=challenge&code_challenge_method=S256" +
            "&authorization_endpoint=https://attacker.example.test/authorize" +
            "&host=attacker.example.test";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/authorize?{query}");
        request.Headers.Host = "attacker.example.test";
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.example.test");
        request.Headers.TryAddWithoutValidation("Forwarded", "host=attacker.example.test;proto=http");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("https://login.example.test/authorize", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("https://attacker.example.test/callback")]
    [InlineData("https://client.example.test/callback/")]
    [InlineData("http://client.example.test/callback")]
    [InlineData("https://CLIENT.example.test/callback")]
    [InlineData("https://client.example.test/callback?x=1")]
    [InlineData("https://client.example.test/callback#fragment")]
    public async Task InvalidOrNearMatchRedirectUrisAreRejectedWithoutRedirecting(string redirectUri)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&code_challenge=challenge&code_challenge_method=S256";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task ClientSubstitutionIsRejectedWithoutRedirecting()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=attacker-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Theory]
    [InlineData("token")]
    [InlineData("id_token")]
    [InlineData("code token")]
    public async Task ResponseTypeInjectionRedirectsWithBoundedError(string responseType)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type={Uri.EscapeDataString(responseType)}&code_challenge=challenge&code_challenge_method=S256&state=preserved";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertRedirectedError(response, "unsupported_response_type", "preserved");
        await application.StopAsync();
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("s256")]
    public async Task PkceDowngradeOrMissingMethodRedirectsWithBoundedError(string method)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method={Uri.EscapeDataString(method)}";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertRedirectedError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingOrEmptyCodeChallengeRedirectsWithBoundedError()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=&code_challenge_method=S256";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertRedirectedError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task ScopeOutsideAllowlistRedirectsWithBoundedErrorAndIsNeverRewritten()
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments.Add("--Bridge:AllowedScopes:0");
            arguments.Add("mcp.read");
        });
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256&scope=mcp.write";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertRedirectedError(response, "invalid_scope");
        await application.StopAsync();
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("redirect_uri")]
    [InlineData("response_type")]
    [InlineData("code_challenge")]
    [InlineData("code_challenge_method")]
    [InlineData("scope")]
    [InlineData("state")]
    public async Task DuplicatedSecurityParametersFailClosedWithoutRedirecting(string duplicated)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256&scope=mcp.read&state=xyz&{duplicated}=second-value";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task ConflictingDuplicateRedirectUriValuesFailClosedEvenWhenOneIsValid()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&redirect_uri=https://attacker.example.test/callback&response_type=code&code_challenge=challenge&code_challenge_method=S256";
        using var response = await client.GetAsync($"/authorize?{query}");

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task MalformedPercentEncodingDoesNotCrashOrRedirectUnsafely()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=broken%zzvalue&code_challenge_method=S256";
        using var response = await client.GetAsync($"/authorize?{query}");

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.BadRequest);
        if (response.Headers.Location is { } location)
        {
            Assert.DoesNotContain('\r', location.OriginalString);
            Assert.DoesNotContain('\n', location.OriginalString);
        }
        await application.StopAsync();
    }

    [Fact]
    public async Task EncodedCrlfInStateIsForwardedAsAnOpaqueValueWithoutHeaderInjection()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var poisonedState = Uri.EscapeDataString("value\r\nX-Injected: evil");
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256&state={poisonedState}";
        using var response = await client.GetAsync($"/authorize?{query}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.DoesNotContain("X-Injected", response.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);
        var forwarded = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("value\r\nX-Injected: evil", forwarded["state"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task AuthorizationIsRateLimited()
    {
        await using var application = BridgeContractHost.Create(permitLimit: 1);
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256";
        using var first = await client.GetAsync($"/authorize?{query}");
        using var second = await client.GetAsync($"/authorize?{query}");

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task CancelledRequestsAreAbortedWithoutServerSideRetry()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = NonRedirectingClient(application);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = $"client_id=fictional-client&redirect_uri={Uri.EscapeDataString(Redirect)}&response_type=code&code_challenge=challenge&code_challenge_method=S256";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync($"/authorize?{query}", cancellation.Token));
        await application.StopAsync();
    }

    private static HttpClient NonRedirectingClient(WebApplication application) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(application.Urls.Single()) };

    private static void AssertBoundedJsonError(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal(error, document.RootElement.GetProperty("error").GetString());
        Assert.Null(response.Headers.Location);
    }

    private static void AssertRedirectedError(HttpResponseMessage response, string error, string? expectedState = null)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var forwarded = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(error, forwarded["error"]);
        Assert.True(forwarded.ContainsKey("error_description"));
        if (expectedState is not null)
        {
            Assert.Equal(expectedState, forwarded["state"]);
        }
    }
}
