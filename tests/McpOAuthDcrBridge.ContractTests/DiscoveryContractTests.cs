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

    [Fact]
    public async Task MetadataDocumentsAreExactAndIndependentOfCallerControlledInput()
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
        using var ordinaryProtected = await client.GetAsync("/.well-known/oauth-protected-resource");
        using var ordinaryAuthorization = await client.GetAsync("/.well-known/oauth-authorization-server");
        using var poisonedProtected = await SendPoisonedMetadataRequestAsync(client, "/.well-known/oauth-protected-resource");
        using var poisonedAuthorization = await SendPoisonedMetadataRequestAsync(client, "/.well-known/oauth-authorization-server");

        Assert.Equal("{\"resource\":\"https://bridge.example.test/mcp\",\"authorization_servers\":[\"https://bridge.example.test/\"],\"scopes_supported\":[\"scope-a\",\"scope-b\"],\"bearer_methods_supported\":[\"header\"]}", await ordinaryProtected.Content.ReadAsStringAsync());
        Assert.Equal("{\"issuer\":\"https://bridge.example.test/\",\"registration_endpoint\":\"https://bridge.example.test/register\",\"authorization_endpoint\":\"https://bridge.example.test/authorize\",\"token_endpoint\":\"https://bridge.example.test/token\",\"response_types_supported\":[\"code\"],\"grant_types_supported\":[\"authorization_code\",\"refresh_token\"],\"token_endpoint_auth_methods_supported\":[\"none\"],\"code_challenge_methods_supported\":[\"S256\"]}", await ordinaryAuthorization.Content.ReadAsStringAsync());
        await AssertSameMetadataResponseAsync(ordinaryProtected, poisonedProtected);
        await AssertSameMetadataResponseAsync(ordinaryAuthorization, poisonedAuthorization);
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

        AssertChallenge(response);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task McpChallengesMissingAuthorizationForEverySupportedMethod(string method)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/mcp"));

        AssertChallenge(response);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task McpChallengeUsesTheExactCanonicalMetadataUrlForEscapedBasePaths(string method)
    {
        await using var application = BridgeContractHost.Create(configure: arguments =>
        {
            arguments[arguments.IndexOf("https://bridge.example.test")] = "https://bridge.example.test/bridge%20path/";
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/mcp"));

        AssertChallenge(response, "https://bridge.example.test/bridge%20path/.well-known/oauth-protected-resource");
        await application.StopAsync();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task ValidBearerCredentialsDoNotReceiveTheLocalChallenge(string method)
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer abc.DEF_123+/==");
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
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

    [Fact]
    public async Task DiscoveryDoesNotRouteNearMissPathsOrUnexpectedMcpMethods()
    {
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var nearMiss = await client.GetAsync("/.well-known/oauth-protected-resource/extra");
        using var mcpMethod = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/mcp"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, nearMiss.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, mcpMethod.StatusCode);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-authorization-server")]
    public async Task DiscoveryRejectsDeclaredAndChunkedBodiesWithoutEchoingThem(string path)
    {
        const string canary = "discovery-body-canary-6b32";
        await using var application = BridgeContractHost.Create();
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var declared = new HttpRequestMessage(HttpMethod.Get, path) { Content = new StringContent(canary) };
        using var chunkedContent = new StringContent(canary);
        chunkedContent.Headers.ContentLength = null;
        using var chunked = new HttpRequestMessage(HttpMethod.Get, path) { Content = chunkedContent };
        chunked.Headers.TransferEncodingChunked = true;
        using var declaredResponse = await client.SendAsync(declared);
        using var chunkedResponse = await client.SendAsync(chunked);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, declaredResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, chunkedResponse.StatusCode);
        Assert.Null(declaredResponse.Content.Headers.ContentType);
        Assert.Null(chunkedResponse.Content.Headers.ContentType);
        Assert.Null(declaredResponse.Headers.CacheControl);
        Assert.Null(chunkedResponse.Headers.CacheControl);
        Assert.Empty(await declaredResponse.Content.ReadAsStringAsync());
        Assert.Empty(await chunkedResponse.Content.ReadAsStringAsync());
        await application.StopAsync();
    }

    private static async Task<HttpResponseMessage> SendPoisonedMetadataRequestAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?caller=untrusted");
        request.Headers.Host = "attacker.example.test";
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.example.test");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        request.Headers.TryAddWithoutValidation("Forwarded", "for=192.0.2.1;host=attacker.example.test;proto=http");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer caller-identity-canary");
        return await client.SendAsync(request);
    }

    private static async Task AssertSameMetadataResponseAsync(HttpResponseMessage ordinary, HttpResponseMessage poisoned)
    {
        Assert.Equal(ordinary.StatusCode, poisoned.StatusCode);
        Assert.Equal(ordinary.Content.Headers.ContentType, poisoned.Content.Headers.ContentType);
        Assert.Equal(ordinary.Headers.CacheControl, poisoned.Headers.CacheControl);
        Assert.Equal(await ordinary.Content.ReadAsStringAsync(), await poisoned.Content.ReadAsStringAsync());
    }

    private static void AssertChallenge(HttpResponseMessage response, string metadataUrl = "https://bridge.example.test/.well-known/oauth-protected-resource")
    {
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal($"Bearer resource_metadata=\"{metadataUrl}\"", response.Headers.WwwAuthenticate.Single()!.ToString());
        Assert.Empty(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }
}
