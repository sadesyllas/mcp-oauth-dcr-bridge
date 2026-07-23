using McpOAuthDcrBridge.Telemetry;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class TelemetryRedactorTests
{
    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("PATCH", "OTHER")]
    public void TelemetryDimensionsAreBounded(string method, string expected) => Assert.Equal(expected, TelemetryRedactor.HttpMethod(method));

    [Fact]
    public void ResultCategoriesAreBounded() => Assert.Equal("5xx", TelemetryRedactor.ResultCategory(999));
}
