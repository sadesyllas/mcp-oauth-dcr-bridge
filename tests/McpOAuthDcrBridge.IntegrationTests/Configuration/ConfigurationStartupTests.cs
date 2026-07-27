using McpOAuthDcrBridge;
using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed class ConfigurationStartupTests
{
    private static readonly string ValidCertificatePath = TestCertificates.WriteTemporaryPfx(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));

    public static IEnumerable<object?[]> ValidCredentialModes()
    {
        yield return ["none", null, null];
        yield return ["client_secret_post", "integration-secret", null];
        yield return ["client_secret_basic", "integration-secret", null];
        yield return ["private_key_jwt", null, ValidCertificatePath];
    }

    [Theory]
    [MemberData(nameof(ValidCredentialModes))]
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
            ["Bridge:Upstream:McpHeaders:0:Name"] = "X-Static",
            ["Bridge:Upstream:McpHeaders:0:Values:0"] = "original-header-value",
            ["Bridge:AllowedRedirectUris:0"] = "https://client.example.test/callback",
        });
        await using var application = BridgeApplication.Build(["--urls", "http://127.0.0.1:0"], configuration);
        const int requestCount = 12;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new ConcurrentQueue<(string Before, string After)>();
        var enteredCount = 0;
        application.MapGet("/_test/options", async (BridgeOptions options, HttpContext context) =>
        {
            var before = Snapshot(options);
            if (Interlocked.Increment(ref enteredCount) == requestCount)
            {
                entered.TrySetResult();
            }

            await release.Task.WaitAsync(context.RequestAborted);
            snapshots.Enqueue((before, Snapshot(options)));
            return Results.NoContent();
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        var requests = Enumerable.Range(0, requestCount).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/options");
            request.Headers.Host = "attacker.example.test";
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attacker.example.test");
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
            request.Headers.TryAddWithoutValidation("Forwarded", "host=attacker.example.test;proto=http");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        configuration["Bridge:ExternalBaseUrl"] = "https://attacker.example.test/";
        configuration["Bridge:Upstream:AuthorizationEndpoint"] = "https://attacker.example.test/authorize";
        configuration["Bridge:Upstream:TokenEndpoint"] = "https://attacker.example.test/token";
        configuration["Bridge:Upstream:McpUrl"] = "https://attacker.example.test/mcp";
        configuration["Bridge:Upstream:ClientAuthentication:ClientSecret"] = "mutated-secret";
        configuration["Bridge:Upstream:McpHeaders:0:Values:0"] = "mutated-header-value";
        ((IConfigurationRoot)configuration).Reload();
        release.TrySetResult();
        var documents = await Task.WhenAll(requests);

        Assert.All(documents, document => Assert.Empty(document));
        Assert.Equal(requestCount, snapshots.Count);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(snapshot.Before, snapshot.After);
            Assert.Contains("https://bridge.example.test/", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("https://login.example.test/authorize", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("https://login.example.test/token", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("https://mcp.example.test/streamable", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("fixed-client", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("ClientSecretPost", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("original-secret", snapshot.Before, StringComparison.Ordinal);
            Assert.Contains("original-header-value", snapshot.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("attacker.example.test", snapshot.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("mutated-secret", snapshot.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("mutated-header-value", snapshot.Before, StringComparison.Ordinal);
        });
        await application.StopAsync();
    }

    private static string Snapshot(BridgeOptions options) => string.Join('|',
        options.ExternalBaseUri,
        options.IssuerUri,
        options.McpResourceUri,
        options.RegistrationUri,
        options.AuthorizationUri,
        options.TokenUri,
        options.UpstreamAuthorizationEndpoint,
        options.UpstreamTokenEndpoint,
        options.UpstreamMcpUri,
        options.ClientId,
        options.ClientAuthentication.Method,
        options.ClientAuthentication.ClientSecret,
        options.ClientAuthentication.CertificatePath,
        string.Join(',', options.AllowedRedirectUris.Order()),
        string.Join(',', options.AllowedScopes.Order()),
        options.Limits.DcrRequestBodyBytes,
        options.Limits.TokenRequestBodyBytes,
        options.Limits.OAuthTimeout,
        options.Limits.McpActivityTimeout,
        options.Limits.ShutdownDrainTimeout,
        options.Limits.RateLimitPermitLimit,
        options.Limits.RateLimitWindow,
        string.Join(',', options.UpstreamMcpHeaders.OrderBy(header => header.Key, StringComparer.Ordinal).Select(header => $"{header.Key}={string.Join(',', header.Value)}")));
}
