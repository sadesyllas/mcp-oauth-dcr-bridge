using McpOAuthDcrBridge;
using McpOAuthDcrBridge.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed class ConfigurationStartupTests
{
    [Theory]
    [InlineData("none", null, null)]
    [InlineData("client_secret_post", "integration-secret", null)]
    [InlineData("client_secret_basic", "integration-secret", null)]
    [InlineData("private_key_jwt", null, "/run/secrets/integration.pfx")]
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
}
