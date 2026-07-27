using System.Net;
using System.Text;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class McpProxyContractTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task RequestsAreProxiedToTheExactConfiguredUpstreamPathIgnoringTheLocalRoute(string method)
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync("/api/streamable");
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp?channel=one");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        Assert.Equal(method, fakeUpstream.LastMethod);
        Assert.Equal("/api/streamable", fakeUpstream.LastPath);
        Assert.Equal("?channel=one", fakeUpstream.LastQuery);
        await application.StopAsync();
    }

    [Fact]
    public async Task OpaqueBearerTokenIsForwardedUnchangedAndNeverParsed()
    {
        const string canaryToken = "opaque-bearer-canary-4d21f.with.dots";
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {canaryToken}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"Bearer {canaryToken}", fakeUpstream.LastHeaders!["Authorization"].ToString());
        await application.StopAsync();
    }

    [Fact]
    public async Task ResponseStatusContentTypeAndSafeHeadersArePreserved()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/vnd.mcp+json";
            context.Response.Headers["X-Upstream-Custom"] = "upstream-value";
            return context.Response.WriteAsync("{\"ok\":true}");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/vnd.mcp+json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("upstream-value", response.Headers.GetValues("X-Upstream-Custom").Single());
        Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync());
        await application.StopAsync();
    }

    [Fact]
    public async Task McpSessionAndProtocolHeadersSurviveTheRoundTrip()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.Headers["Mcp-Session-Id"] = "session-from-upstream";
            return context.Response.WriteAsync("ok");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", "session-from-client");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "42");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        using var response = await client.SendAsync(request);

        Assert.Equal("session-from-client", fakeUpstream.LastHeaders!["Mcp-Session-Id"].ToString());
        Assert.Equal("42", fakeUpstream.LastHeaders!["Last-Event-ID"].ToString());
        Assert.Equal("2025-06-18", fakeUpstream.LastHeaders!["MCP-Protocol-Version"].ToString());
        Assert.Equal("session-from-upstream", response.Headers.GetValues("Mcp-Session-Id").Single());
        await application.StopAsync();
    }

    [Fact]
    public async Task HopByHopHeadersAreNeverForwardedInEitherDirection()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.Headers["Connection"] = "close";
            context.Response.Headers["Keep-Alive"] = "timeout=5";
            return context.Response.WriteAsync("ok");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.False(fakeUpstream.LastHeaders!.ContainsKey("Keep-Alive"));
        Assert.DoesNotContain("Keep-Alive", response.Headers.Select(header => header.Key), StringComparer.OrdinalIgnoreCase);
        await application.StopAsync();
    }

    [Fact]
    public async Task ConfiguredStaticHeaderIsAddedAndReplacesADownstreamValueCaseInsensitively()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Name");
            arguments.Add("X-Deployment-Context");
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Values:0");
            arguments.Add("configured-value");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        request.Headers.TryAddWithoutValidation("x-deployment-context", "downstream-spoofed-value");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("configured-value", fakeUpstream.LastHeaders!["X-Deployment-Context"].ToString());
        await application.StopAsync();
    }

    [Fact]
    public async Task MultipleConfiguredHeaderValuesAreAllForwarded()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Name");
            arguments.Add("X-Multi");
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Values:0");
            arguments.Add("first");
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Values:1");
            arguments.Add("second");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal("first, second", fakeUpstream.LastHeaders!["X-Multi"].ToString());
        await application.StopAsync();
    }

    [Fact]
    public async Task ZeroConfiguredHeadersLeavesTheRequestUntouched()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        request.Headers.TryAddWithoutValidation("X-Custom", "downstream-value");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("downstream-value", fakeUpstream.LastHeaders!["X-Custom"].ToString());
        await application.StopAsync();
    }

    [Fact]
    public async Task ConfiguredHeaderIsIsolatedFromNonMcpEndpoints()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Name");
            arguments.Add("X-Deployment-Context");
            arguments.Add("--Bridge:Upstream:McpHeaders:0:Values:0");
            arguments.Add("configured-value");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var health = await client.GetAsync("/health/live");
        using var metadata = await client.GetAsync("/.well-known/oauth-protected-resource");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("attacker.example.test")]
    public async Task HostAndForwardingHeaderPoisoningCannotRedirectTheProxyDestination(string spoofedHost)
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        request.Headers.Host = spoofedHost;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", spoofedHost);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        request.Headers.TryAddWithoutValidation("Forwarded", $"host={spoofedHost};proto=http");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("/mcp/../secret")]
    [InlineData("/mcp%2F..%2Fsecret")]
    [InlineData("/mcp/extra")]
    public async Task PathTraversalAndNearMissPathsNeverReachTheProxy(string path)
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(0, fakeUpstream.RequestCount);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamRedirectResponseIsRelayedWithoutBeingFollowed()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "https://attacker.example.test/elsewhere";
            return Task.CompletedTask;
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://attacker.example.test/elsewhere", response.Headers.Location!.ToString());
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamBearerChallengeIsRewrittenToIdentifyBridgeMetadataWhilePreservingErrorAndScope()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"mcp-upstream\", error=\"insufficient_scope\", error_description=\"need more\", scope=\"mcp.write\"";
            return Task.CompletedTask;
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.Single()!.ToString();
        Assert.StartsWith("Bearer ", challenge, StringComparison.Ordinal);
        Assert.Contains("resource_metadata=\"https://bridge.example.test/.well-known/oauth-protected-resource\"", challenge, StringComparison.Ordinal);
        Assert.Contains("error=\"insufficient_scope\"", challenge, StringComparison.Ordinal);
        Assert.Contains("error_description=\"need more\"", challenge, StringComparison.Ordinal);
        Assert.Contains("scope=\"mcp.write\"", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp-upstream", challenge, StringComparison.Ordinal);
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamBearerChallengeWithNoParametersIsRewrittenToBareResourceMetadata()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "Bearer resource_metadata=\"https://bridge.example.test/.well-known/oauth-protected-resource\"",
            response.Headers.WwwAuthenticate.Single()!.ToString());
        await application.StopAsync();
    }

    [Fact]
    public async Task LargeRequestAndResponseBodiesAreRelayedByteForByteWithoutTransformation()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var largeBody = new string('m', 512 * 1024);
        fakeUpstream.OnRequest = context => context.Response.WriteAsync(largeBody);
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(largeBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(largeBody, fakeUpstream.LastBody);
        Assert.Equal(largeBody, await response.Content.ReadAsStringAsync());
        await application.StopAsync();
    }

    [Fact]
    public async Task UpstreamUnavailableMapsToABoundedGatewayErrorWithoutRetry()
    {
        using var unreachable = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        unreachable.Start();
        var port = ((IPEndPoint)unreachable.LocalEndpoint).Port;
        unreachable.Stop();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp($"http://127.0.0.1:{port}/api/streamable");
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.True((int)response.StatusCode is 502 or 503 or 504);
        await application.StopAsync();
    }

    [Fact]
    public async Task NoAutomaticRetryAfterUpstreamServerError()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task MissingBearerCredentialNeverReachesTheUpstream()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fakeUpstream.RequestCount);
        await application.StopAsync();
    }
}
