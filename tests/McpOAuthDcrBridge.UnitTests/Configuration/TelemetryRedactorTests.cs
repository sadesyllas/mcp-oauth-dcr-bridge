using McpOAuthDcrBridge.Telemetry;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class TelemetryRedactorTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Innocuous-Configured-Secret")]
    public void HeaderValuesAreAlwaysRedacted(string name) => Assert.Equal(TelemetryRedactor.RedactedValue, TelemetryRedactor.HeaderValue(name, "telemetry-canary-8c748b"));

    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("PATCH", "OTHER")]
    public void TelemetryDimensionsAreBounded(string method, string expected) => Assert.Equal(expected, TelemetryRedactor.HttpMethod(method));

    [Fact]
    public void ResultCategoriesAreBounded() => Assert.Equal("5xx", TelemetryRedactor.ResultCategory(999));
}
