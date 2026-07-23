using McpOAuthDcrBridge.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class BridgeOptionsFactoryTests
{
    [Fact]
    public void CreateResolvesCanonicalPublicUrisAndFixedOutboundDestinations()
    {
        var options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(), false);

        Assert.Equal("https://bridge.example.test/base/", options.IssuerUri.AbsoluteUri);
        Assert.Equal("https://bridge.example.test/base/mcp", options.McpResourceUri.AbsoluteUri);
        Assert.Equal("https://login.example.test/authorize", options.UpstreamAuthorizationEndpoint.AbsoluteUri);
        Assert.Equal("fictional-client", options.ClientId);
        Assert.Empty(options.AllowedScopes);
        Assert.Equal(32 * 1024, options.Limits.DcrRequestBodyBytes);
        Assert.Equal(16 * 1024, options.Limits.TokenRequestBodyBytes);
    }

    [Theory]
    [InlineData("Bridge:ExternalBaseUrl", null)]
    [InlineData("Bridge:Upstream:AuthorizationEndpoint", "http://login.example.test/authorize")]
    [InlineData("Bridge:Upstream:TokenEndpoint", "https://login.example.test/token?unsafe=true")]
    [InlineData("Bridge:Upstream:McpUrl", "https://user:secret@mcp.example.test/")]
    [InlineData("Bridge:Upstream:ClientId", " ")]
    public void CreateRejectsMissingOrUnsafeRequiredValues(string key, string? value)
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values[key] = value), false));

        Assert.Contains(key.Split(':').Last(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAllowsHttpOnlyForExplicitDevelopmentSetting()
    {
        var configuration = ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:AllowHttpForLocalDevelopment"] = "true";
            values["Bridge:ExternalBaseUrl"] = "http://127.0.0.1:5100";
            values["Bridge:Upstream:AuthorizationEndpoint"] = "http://127.0.0.1:5101/authorize";
            values["Bridge:Upstream:TokenEndpoint"] = "http://127.0.0.1:5101/token";
            values["Bridge:Upstream:McpUrl"] = "http://127.0.0.1:5102/mcp";
            values["Bridge:AllowedRedirectUris:0"] = "http://127.0.0.1:5103/callback";
        });

        Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, false));
        Assert.Equal("http", BridgeOptionsFactory.Create(configuration, true).ExternalBaseUri.Scheme);
    }

    [Theory]
    [InlineData("https://client.example.test/callback#fragment")]
    [InlineData("https://client.example.test/callback?query=true")]
    public void CreateRejectsNonExactRedirectUris(string redirectUri)
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:0"] = redirectUri), false));

        Assert.Contains("AllowedRedirectUris", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsDuplicateRedirectUrisAndInvalidScopes()
    {
        var duplicateRedirect = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:1"] = values["Bridge:AllowedRedirectUris:0"]), false));
        var invalidScope = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedScopes:0"] = "two words"), false));

        Assert.Contains("AllowedRedirectUris", duplicateRedirect.Message, StringComparison.Ordinal);
        Assert.Contains("AllowedScopes", invalidScope.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", null, null, true)]
    [InlineData("client_secret_post", "secret", null, true)]
    [InlineData("client_secret_basic", "secret", null, true)]
    [InlineData("private_key_jwt", null, "/run/secrets/client.pfx", true)]
    [InlineData("none", "secret", null, false)]
    [InlineData("client_secret_post", null, null, false)]
    [InlineData("private_key_jwt", "secret", null, false)]
    public void CreateValidatesCredentialCombinations(string method, string? secret, string? certificatePath, bool valid)
    {
        var configuration = ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:ClientAuthentication:Method"] = method;
            values["Bridge:Upstream:ClientAuthentication:ClientSecret"] = secret;
            values["Bridge:Upstream:ClientAuthentication:CertificatePath"] = certificatePath;
        });

        if (valid)
        {
            Assert.Equal(method, BridgeOptionsFactory.Create(configuration, false).ClientAuthentication.Method.ToString().ToSnakeCase());
        }
        else
        {
            Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, false));
        }
    }

    [Fact]
    public void CreateRejectsForbiddenAndDuplicateHeadersWithoutLeakingTheirValues()
    {
        const string canary = "header-canary-2fc649a4";
        var forbidden = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:McpHeaders:0:Name"] = "Authorization";
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = canary;
        }), false));
        var duplicate = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:McpHeaders:0:Name"] = "X-Static";
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = "one";
            values["Bridge:Upstream:McpHeaders:1:Name"] = "x-static";
            values["Bridge:Upstream:McpHeaders:1:Values:0"] = "two";
        }), false));

        Assert.DoesNotContain(canary, forbidden.ToString(), StringComparison.Ordinal);
        Assert.Contains("McpHeaders", duplicate.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DcrRequestBodyBytes", "1023")]
    [InlineData("TokenRequestBodyBytes", "1048577")]
    [InlineData("OAuthTimeoutSeconds", "0")]
    [InlineData("McpActivityTimeoutSeconds", "3601")]
    [InlineData("ShutdownDrainTimeoutSeconds", "301")]
    [InlineData("RateLimitPermitLimit", "0")]
    [InlineData("RateLimitWindowSeconds", "3601")]
    public void CreateRejectsOutOfBoundsLimits(string key, string value)
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values[$"Bridge:Limits:{key}"] = value), false));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvedOptionsRemainImmutableDuringConcurrentReads()
    {
        var options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:McpHeaders:0:Name"] = "X-Static";
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = "stable";
        }), false);
        var expected = options.McpResourceUri.AbsoluteUri;

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            Assert.Equal(expected, options.McpResourceUri.AbsoluteUri);
            Assert.Equal("stable", options.UpstreamMcpHeaders["X-Static"][0]);
        })));
    }
}
