using McpOAuthDcrBridge.Configuration;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
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
        Assert.Equal("https://bridge.example.test/base/register", options.RegistrationUri.AbsoluteUri);
        Assert.Equal("https://bridge.example.test/base/authorize", options.AuthorizationUri.AbsoluteUri);
        Assert.Equal("https://bridge.example.test/base/token", options.TokenUri.AbsoluteUri);
        Assert.Equal("https://login.example.test/authorize", options.UpstreamAuthorizationEndpoint.AbsoluteUri);
        Assert.Equal("https://login.example.test/token", options.UpstreamTokenEndpoint.AbsoluteUri);
        Assert.Equal("https://mcp.example.test/streamable", options.UpstreamMcpUri.AbsoluteUri);
        Assert.Equal("fictional-client", options.ClientId);
        Assert.Equal(UpstreamClientAuthenticationMethod.None, options.ClientAuthentication.Method);
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

    [Theory]
    [InlineData("Bridge:ExternalBaseUrl")]
    [InlineData("Bridge:Upstream:AuthorizationEndpoint")]
    [InlineData("Bridge:Upstream:TokenEndpoint")]
    [InlineData("Bridge:Upstream:McpUrl")]
    [InlineData("Bridge:Upstream:ClientId")]
    [InlineData("Bridge:Upstream:ClientAuthentication:Method")]
    public void CreateRejectsEveryMissingRequiredKey(string key)
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values[key] = string.Empty), false));

        Assert.Contains(key.Split(':').Last(), exception.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object?[]> FixedUriRules()
    {
        var keys = new[]
        {
            "Bridge:ExternalBaseUrl",
            "Bridge:Upstream:AuthorizationEndpoint",
            "Bridge:Upstream:TokenEndpoint",
            "Bridge:Upstream:McpUrl",
        };
        foreach (var key in keys)
        {
            yield return [key, string.Empty, false, false];
            yield return [key, "/relative", false, false];
            yield return [key, "http://remote.example.test/path", false, false];
            yield return [key, "https://user:password@example.test/path", false, false];
            yield return [key, "https://example.test/path?query=true", false, false];
            yield return [key, "https://example.test/path#fragment", false, false];
            yield return [key, "http://127.0.0.1:5000/path", true, true];
            yield return [key, "http://localhost.evil.test/path", true, false];
        }
    }

    [Theory]
    [MemberData(nameof(FixedUriRules))]
    public void CreateAppliesEveryFixedUriRule(string key, string value, bool development, bool valid)
    {
        var configuration = ValidBridgeConfiguration.Create(values =>
        {
            values[key] = value;
            values["Bridge:AllowHttpForLocalDevelopment"] = "true";
        });

        if (valid)
        {
            Assert.NotNull(BridgeOptionsFactory.Create(configuration, development));
        }
        else
        {
            Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, development));
        }
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

    [Fact]
    public void CreatePreservesQueryBearingRedirectUrisWithoutNormalization()
    {
        const string redirectUri = "https://CLIENT.example.test:443/callback?return=%2Fone";
        var options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:0"] = redirectUri), false);

        Assert.Contains(redirectUri, options.AllowedRedirectUris);
    }

    [Theory]
    [InlineData("https://client.example.test/callback#fragment")]
    [InlineData("https://user@client.example.test/callback")]
    public void CreateRejectsUnsafeRedirectUris(string redirectUri)
    {
        var exception = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:0"] = redirectUri), false));

        Assert.Contains("AllowedRedirectUris", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://CLIENT.example.test/callback", true)]
    [InlineData("https://client.example.test:443/callback", true)]
    [InlineData("https://client.example.test/one/../callback", true)]
    [InlineData("https://client.example.test/callback?text=one%2Ftwo", true)]
    [InlineData("https://client.example.test/callback#fragment", false)]
    [InlineData("https://user@client.example.test/callback", false)]
    [InlineData("http://client.example.test/callback", false)]
    public void CreateAppliesExactRedirectBoundaryRules(string redirectUri, bool valid)
    {
        var configuration = ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:0"] = redirectUri);

        if (valid)
        {
            Assert.Contains(redirectUri, BridgeOptionsFactory.Create(configuration, false).AllowedRedirectUris);
        }
        else
        {
            Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, false));
        }
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
    [InlineData("http://remote.example.test/callback")]
    [InlineData("http://192.0.2.2/callback")]
    [InlineData("http://[::2]/callback")]
    [InlineData("http://localhost.evil.test/callback")]
    public void CreateRejectsNonLoopbackDevelopmentHttpUris(string uri)
    {
        var configuration = ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:AllowHttpForLocalDevelopment"] = "true";
            values["Bridge:ExternalBaseUrl"] = uri;
        });

        Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, true));
    }

    [Theory]
    [InlineData("valid.scope")]
    [InlineData("quote\"scope")]
    [InlineData("slash\\scope")]
    [InlineData("scope space")]
    [InlineData("scope\u0001")]
    public void CreateEnforcesOAuthScopeGrammar(string scope)
    {
        var configuration = ValidBridgeConfiguration.Create(values => values["Bridge:AllowedScopes:0"] = scope);

        if (scope == "valid.scope")
        {
            Assert.Contains(scope, BridgeOptionsFactory.Create(configuration, false).AllowedScopes);
        }
        else
        {
            Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, false));
        }
    }

    [Theory]
    [InlineData("X-Valid", "safe value", true)]
    [InlineData("Bad Header", "safe value", false)]
    [InlineData("Bad()", "safe value", false)]
    [InlineData("X-Valid", "bad\r\nvalue", false)]
    [InlineData("X-Valid", "obs\u0080text", true)]
    [InlineData("X-Valid", "bad\u0100text", false)]
    [InlineData("X-Valid", "bad\ud800text", false)]
    public void CreateEnforcesHttpHeaderGrammar(string name, string value, bool valid)
    {
        var configuration = ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:McpHeaders:0:Name"] = name;
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = value;
        });

        if (valid)
        {
            Assert.Equal(value, BridgeOptionsFactory.Create(configuration, false).UpstreamMcpHeaders[name][0]);
        }
        else
        {
            Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(configuration, false));
        }
    }

    public static IEnumerable<object?[]> CredentialCombinations()
    {
        var methods = new[] { "none", "client_secret_post", "client_secret_basic", "private_key_jwt" };
        var credentials = new[]
        {
            (Secret: (string?)null, CertificatePath: (string?)null),
            (Secret: "secret", CertificatePath: (string?)null),
            (Secret: (string?)null, CertificatePath: "/run/secrets/client.pfx"),
            (Secret: "secret", CertificatePath: "/run/secrets/client.pfx"),
        };
        foreach (var method in methods)
        {
            foreach (var credential in credentials)
            {
                var valid = method switch
                {
                    "none" => credential is { Secret: null, CertificatePath: null },
                    "client_secret_post" or "client_secret_basic" => credential is { Secret: not null, CertificatePath: null },
                    "private_key_jwt" => credential is { Secret: null, CertificatePath: not null },
                    _ => false,
                };
                yield return [method, credential.Secret, credential.CertificatePath, valid];
            }
        }

        yield return ["unrecognized", null, null, false];
    }

    [Theory]
    [MemberData(nameof(CredentialCombinations))]
    public void CreateValidatesEveryCredentialCombination(string method, string? secret, string? certificatePath, bool valid)
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

    [Fact]
    public void OptionsDiagnosticsAndConfigurationFailuresDoNotExposeCredentialsOrHeaderValues()
    {
        const string secretCanary = "client-secret-canary-1f39";
        const string certificateCanary = "/run/secrets/certificate-canary-1f39.pfx";
        const string headerCanary = "header-value-canary-1f39";
        var options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:ClientAuthentication:Method"] = "client_secret_post";
            values["Bridge:Upstream:ClientAuthentication:ClientSecret"] = secretCanary;
            values["Bridge:Upstream:McpHeaders:0:Name"] = "X-Canary";
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = headerCanary;
        }), false);
        var certificateOptions = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:ClientAuthentication:Method"] = "private_key_jwt";
            values["Bridge:Upstream:ClientAuthentication:CertificatePath"] = certificateCanary;
        }), false);
        var failure = Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values =>
        {
            values["Bridge:Upstream:McpHeaders:0:Name"] = "Authorization";
            values["Bridge:Upstream:McpHeaders:0:Values:0"] = headerCanary;
        }), false));

        var representations = new[]
        {
            JsonSerializer.Serialize(options),
            JsonSerializer.Serialize(options.ClientAuthentication),
            JsonSerializer.Serialize(certificateOptions),
            JsonSerializer.Serialize(certificateOptions.ClientAuthentication),
            options.ToString() ?? string.Empty,
            certificateOptions.ToString() ?? string.Empty,
            failure.ToString(),
        };
        foreach (var representation in representations)
        {
            Assert.DoesNotContain(secretCanary, representation, StringComparison.Ordinal);
            Assert.DoesNotContain(certificateCanary, representation, StringComparison.Ordinal);
            Assert.DoesNotContain(headerCanary, representation, StringComparison.Ordinal);
        }
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

    [Theory]
    [InlineData("DcrRequestBodyBytes", "1024")]
    [InlineData("DcrRequestBodyBytes", "1048576")]
    [InlineData("TokenRequestBodyBytes", "1024")]
    [InlineData("TokenRequestBodyBytes", "1048576")]
    [InlineData("OAuthTimeoutSeconds", "1")]
    [InlineData("OAuthTimeoutSeconds", "120")]
    [InlineData("McpActivityTimeoutSeconds", "1")]
    [InlineData("McpActivityTimeoutSeconds", "3600")]
    [InlineData("ShutdownDrainTimeoutSeconds", "1")]
    [InlineData("ShutdownDrainTimeoutSeconds", "300")]
    [InlineData("RateLimitPermitLimit", "1")]
    [InlineData("RateLimitPermitLimit", "10000")]
    [InlineData("RateLimitWindowSeconds", "1")]
    [InlineData("RateLimitWindowSeconds", "3600")]
    public void CreateAcceptsLimitBoundaries(string key, string value)
    {
        var options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values[$"Bridge:Limits:{key}"] = value), false);

        Assert.NotNull(options.Limits);
    }

    [Fact]
    public void CreateUsesEveryDocumentedLimitDefault()
    {
        var limits = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(), false).Limits;

        Assert.Equal(32 * 1024, limits.DcrRequestBodyBytes);
        Assert.Equal(16 * 1024, limits.TokenRequestBodyBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.OAuthTimeout);
        Assert.Equal(TimeSpan.FromSeconds(300), limits.McpActivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.ShutdownDrainTimeout);
        Assert.Equal(100, limits.RateLimitPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), limits.RateLimitWindow);
    }

    [Theory]
    [InlineData("DcrRequestBodyBytes")]
    [InlineData("TokenRequestBodyBytes")]
    [InlineData("OAuthTimeoutSeconds")]
    [InlineData("McpActivityTimeoutSeconds")]
    [InlineData("ShutdownDrainTimeoutSeconds")]
    [InlineData("RateLimitPermitLimit")]
    [InlineData("RateLimitWindowSeconds")]
    public void CreateRejectsNonNumericLimits(string key)
    {
        Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values[$"Bridge:Limits:{key}"] = "not-a-number"), false));
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

    [Fact]
    public async Task ResolvedOptionsIgnoreConcurrentConfigurationProviderMutation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Bridge:ExternalBaseUrl"] = "https://bridge.example.test/",
            ["Bridge:Upstream:AuthorizationEndpoint"] = "https://login.example.test/authorize",
            ["Bridge:Upstream:TokenEndpoint"] = "https://login.example.test/token",
            ["Bridge:Upstream:McpUrl"] = "https://mcp.example.test/streamable",
            ["Bridge:Upstream:ClientId"] = "fixed-client",
            ["Bridge:Upstream:ClientAuthentication:Method"] = "client_secret_post",
            ["Bridge:Upstream:ClientAuthentication:ClientSecret"] = "original-secret",
            ["Bridge:AllowedRedirectUris:0"] = "https://client.example.test/callback",
        };
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        var options = BridgeOptionsFactory.Create(configuration, false);
        var expectedResource = options.McpResourceUri.AbsoluteUri;

        var reads = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            Assert.Equal(expectedResource, options.McpResourceUri.AbsoluteUri);
            Assert.Equal("original-secret", options.ClientAuthentication.ClientSecret);
        }));
        configuration["Bridge:ExternalBaseUrl"] = "https://attacker.example.test/";
        configuration["Bridge:Upstream:ClientAuthentication:ClientSecret"] = "mutated-secret";
        await Task.WhenAll(reads);

        Assert.Equal("https://bridge.example.test/mcp", options.McpResourceUri.AbsoluteUri);
        Assert.Equal("original-secret", options.ClientAuthentication.ClientSecret);
    }
}
