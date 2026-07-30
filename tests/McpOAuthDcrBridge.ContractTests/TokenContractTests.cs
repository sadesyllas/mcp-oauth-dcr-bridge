using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class TokenContractTests
{
    private const string Redirect = "https://client.example.test/callback";

    [Fact]
    public async Task AuthorizationCodeExchangeSucceedsUnderNoneAuthenticationAndForwardsFormUnchanged()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        Assert.Equal("auth-code-123", fakeUpstream.LastForm!["code"]);
        Assert.Equal("verifier-abc", fakeUpstream.LastForm!["code_verifier"]);
        Assert.Equal(Redirect, fakeUpstream.LastForm!["redirect_uri"]);
        Assert.Equal("fictional-client", fakeUpstream.LastForm!["client_id"]);
        Assert.False(fakeUpstream.LastForm!.ContainsKey("client_secret"));
        Assert.Equal(string.Empty, fakeUpstream.LastAuthorizationHeader);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("opaque-upstream-token", document.RootElement.GetProperty("access_token").GetString());
        await application.StopAsync();
    }

    [Fact]
    public async Task AuthorizationCodeExchangeAddsSecretAsFormFieldExactlyOnceUnderClientSecretPost()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:Method");
            arguments.Add("client_secret_post");
            arguments.Add("--Bridge:Upstream:ClientAuthentication:ClientSecret");
            arguments.Add("upstream-secret-value");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new StringValues("upstream-secret-value"), fakeUpstream.LastForm!["client_secret"]);
        Assert.Equal(string.Empty, fakeUpstream.LastAuthorizationHeader);
        await application.StopAsync();
    }

    [Fact]
    public async Task AuthorizationCodeExchangeAddsBasicAuthorizationHeaderUnderClientSecretBasic()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:Method");
            arguments.Add("client_secret_basic");
            arguments.Add("--Bridge:Upstream:ClientAuthentication:ClientSecret");
            arguments.Add("upstream-secret-value");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(fakeUpstream.LastForm!.ContainsKey("client_secret"));
        Assert.StartsWith("Basic ", fakeUpstream.LastAuthorizationHeader, StringComparison.Ordinal);
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(fakeUpstream.LastAuthorizationHeader!["Basic ".Length..]));
        Assert.Equal("fictional-client:upstream-secret-value", decoded);
        await application.StopAsync();
    }

    [Fact]
    public async Task RefreshTokenExchangeForwardsTokenUnchangedAndIsNeverInspected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "fictional-client",
            ["refresh_token"] = "opaque-refresh-token-xyz",
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("opaque-refresh-token-xyz", fakeUpstream.LastForm!["refresh_token"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task ReplayedAuthorizationCodeExchangeIsForwardedIndependentlyEachTime()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var first = await client.PostAsync("/token", new FormUrlEncodedContent(form));
        using var replay = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task ExtensionAndRepeatedFieldsArePreservedExactlyIncludingEmptyValues()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        const string body = "grant_type=refresh_token&client_id=fictional-client&refresh_token=abc" +
            "&scope=mcp.read%20mcp.write&resource=https%3A%2F%2Fmcp.example.test%2Fone&resource=https%3A%2F%2Fmcp.example.test%2Ftwo&empty_extension=";
        using var response = await client.PostAsync("/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("mcp.read mcp.write", fakeUpstream.LastForm!["scope"]);
        Assert.Equal(new StringValues(["https://mcp.example.test/one", "https://mcp.example.test/two"]), fakeUpstream.LastForm!["resource"]);
        Assert.Equal(string.Empty, fakeUpstream.LastForm!["empty_extension"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamOAuthErrorIsRelayedVerbatimWithoutReinterpretation()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"error\":\"invalid_grant\",\"error_description\":\"code expired\"}");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "expired-code",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid_grant\",\"error_description\":\"code expired\"}", await response.Content.ReadAsStringAsync());
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamJwtShapedTokenAndRotatingRefreshTokenAreRelayedVerbatim()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        const string jwtLikeToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJhbGljZSJ9.signature";
        fakeUpstream.OnRequest = context => context.Response.WriteAsJsonAsync(new
        {
            access_token = jwtLikeToken,
            token_type = "Bearer",
            refresh_token = "rotated-refresh-token-2",
            expires_in = 120,
            extension_field = "vendor-value",
        });
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "old-refresh-token" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(jwtLikeToken, document.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("rotated-refresh-token-2", document.RootElement.GetProperty("refresh_token").GetString());
        Assert.Equal("vendor-value", document.RootElement.GetProperty("extension_field").GetString());
        await application.StopAsync();
    }

    [Fact]
    public async Task WrongContentTypeIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsync("/token", new StringContent("{\"grant_type\":\"authorization_code\"}", Encoding.UTF8, "application/json"));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("implicit")]
    [InlineData("client_credentials")]
    [InlineData("password")]
    public async Task UnsupportedGrantTypeIsRejected(string grantType)
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = grantType, ["client_id"] = "fictional-client" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "unsupported_grant_type");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingGrantTypeIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["client_id"] = "fictional-client" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task ClientSubstitutionIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "attacker-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_client");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("https://attacker.example.test/callback")]
    [InlineData("https://client.example.test/callback/")]
    public async Task RedirectMismatchIsRejected(string redirectUri)
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = redirectUri,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_grant");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingCodeVerifierIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingCodeIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_request");
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingRefreshTokenIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("grant_type", "grant_type=refresh_token&grant_type=refresh_token&client_id=fictional-client&refresh_token=abc")]
    [InlineData("client_id", "grant_type=refresh_token&client_id=fictional-client&client_id=fictional-client&refresh_token=abc")]
    [InlineData("refresh_token", "grant_type=refresh_token&client_id=fictional-client&refresh_token=abc&refresh_token=def")]
    [InlineData("scope", "grant_type=refresh_token&client_id=fictional-client&refresh_token=abc&scope=a&scope=b")]
    public async Task DuplicatedSecurityParametersAreRejected(string _, string body)
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsync("/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task DownstreamAuthorizationHeaderIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = new FormUrlEncodedContent(form) };
        request.Headers.TryAddWithoutValidation("Authorization", "Basic Zm9vOmJhcg==");
        using var response = await client.SendAsync(request);

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("client_secret")]
    [InlineData("client_assertion")]
    [InlineData("client_assertion_type")]
    public async Task SmuggledCredentialFieldsAreRejected(string field)
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc", [field] = "smuggled-canary" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.DoesNotContain("smuggled-canary", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task OversizedDeclaredBodyIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var body = "grant_type=refresh_token&client_id=fictional-client&refresh_token=" + new string('a', 16 * 1024 + 1);
        using var response = await client.PostAsync("/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"));

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task OversizedChunkedBodyIsRejected()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var body = Encoding.UTF8.GetBytes("grant_type=refresh_token&client_id=fictional-client&refresh_token=" + new string('a', 16 * 1024 + 1));
        using var content = new ChunkedContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await client.PostAsync("/token", content);

        AssertBoundedJsonError(response, "invalid_request");
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task MalformedPercentEncodingInASecurityFieldForwardsTheLiteralTextRatherThanCrashing()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        const string body = "grant_type=refresh_token&client_id=fictional-client&refresh_token=a%zzb";
        using var response = await client.PostAsync("/token", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        Assert.Equal("a%zzb", fakeUpstream.LastForm!["refresh_token"]);
        await application.StopAsync();
    }

    [Fact]
    public async Task BodyOfOnlySeparatorsIsRejectedWithoutReachingUpstream()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.PostAsync("/token", new StringContent("&&&===", Encoding.UTF8, "application/x-www-form-urlencoded"));

        AssertBoundedJsonError(response, "invalid_client");
        Assert.DoesNotContain("===", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task InvalidUtf8BytesInASecurityFieldForwardTheReplacementTextRatherThanCrashing()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var prefix = Encoding.UTF8.GetBytes("grant_type=refresh_token&client_id=fictional-client&refresh_token=");
        byte[] invalidUtf8 = [0xFF, 0xFE, 0x80];
        var body = new ByteArrayContent([.. prefix, .. invalidUtf8]);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await client.PostAsync("/token", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        Assert.False(string.IsNullOrEmpty(fakeUpstream.LastForm!["refresh_token"].ToString()));
        await application.StopAsync();
    }

    [Fact]
    public async Task TokenEndpointIsRateLimited()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, permitLimit: 1);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };
        using var first = await client.PostAsync("/token", new FormUrlEncodedContent(form));
        using var second = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamTimeoutMapsToGatewayTimeoutWithoutRetry()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        fakeUpstream.OnRequest = async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { access_token = "too-late" });
        };
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:OAuthTimeoutSeconds");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()), Timeout = TimeSpan.FromSeconds(30) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamUnavailableMapsToBadGateway()
    {
        using var unreachable = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        unreachable.Start();
        var port = ((System.Net.IPEndPoint)unreachable.LocalEndpoint).Port;
        unreachable.Stop();
        await using var application = BridgeContractHost.CreateWithUpstreamToken($"http://127.0.0.1:{port}/token");
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task CancelledRequestsAreAbortedWithoutServerSideRetry()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsync("/token", new FormUrlEncodedContent(form), cancellation.Token));
        await application.StopAsync();
    }

    [Fact]
    public async Task NoAutomaticRetryAfterUpstreamServerError()
    {
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        };
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    private static void AssertBoundedJsonError(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal(error, document.RootElement.GetProperty("error").GetString());
    }

    private sealed class ChunkedContent : HttpContent
    {
        private readonly byte[] bytes;

        public ChunkedContent(byte[] bytes) => this.bytes = bytes;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
