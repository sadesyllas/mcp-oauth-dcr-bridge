using McpOAuthDcrBridge.Telemetry;
using Microsoft.Extensions.Logging;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class SafeTelemetryPolicyTests
{
    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, true)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    [InlineData((LogLevel)99, false)]
    public void RequestTelemetryCategoryAllowsOnlyItsRegisteredLevels(LogLevel level, bool expected) =>
        Assert.Equal(expected, SafeTelemetryPolicy.IsEnabled(null, typeof(RequestTelemetryMiddleware).FullName!, level));

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("arbitrary.category")]
    [InlineData("McpOAuthDcrBridge.Telemetry.RequestTelemetryMiddleware.NearMatch")]
    public void UnregisteredCategoriesAreRejectedAtEveryLevel(string category)
    {
        foreach (var level in Enum.GetValues<LogLevel>().Append((LogLevel)99))
        {
            Assert.False(SafeTelemetryPolicy.IsEnabled(null, category, level));
        }
    }

    [Fact]
    public void ConfigurationErrorsContainOnlyTheConfigurationKey()
    {
        var message = SafeTelemetryPolicy.ConfigurationError("Upstream:ClientAuthentication:ClientSecret");

        Assert.Contains("ClientSecret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("telemetry-canary", message, StringComparison.Ordinal);
    }
}
