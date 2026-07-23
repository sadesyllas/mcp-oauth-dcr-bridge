using McpOAuthDcrBridge.Telemetry;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class TelemetryEndpointClassifierTests
{
    [Theory]
    [InlineData("/health/live", "health_live")]
    [InlineData("/health/ready", "health_ready")]
    [InlineData("/.well-known/oauth-protected-resource", "protected_resource_metadata")]
    [InlineData("/.well-known/oauth-authorization-server", "authorization_server_metadata")]
    [InlineData("/register", "registration")]
    [InlineData("/mcp", "mcp")]
    [InlineData("/tool/user-controlled", "other")]
    public void ClassifyReturnsOnlyBoundedCategories(string path, string expected) => Assert.Equal(expected, TelemetryEndpointClassifier.Classify(new PathString(path)));
}
