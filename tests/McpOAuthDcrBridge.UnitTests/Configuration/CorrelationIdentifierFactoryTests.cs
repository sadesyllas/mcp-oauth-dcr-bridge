using McpOAuthDcrBridge.Telemetry;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class CorrelationIdentifierFactoryTests
{
    [Theory]
    [InlineData("client-123")]
    [InlineData("abc.DEF_123-xyz")]
    public void CreatePreservesValidBoundedValues(string candidate) => Assert.Equal(candidate, CorrelationIdentifierFactory.Create(candidate).Value);

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("bad\r\nheader")]
    [InlineData("<script>")]
    public void CreateReplacesInvalidValues(string candidate)
    {
        var result = CorrelationIdentifierFactory.Create(candidate).Value;
        Assert.NotEqual(candidate, result);
        Assert.True(CorrelationIdentifierFactory.IsValid(result));
    }

    [Fact]
    public void CreateReplacesOversizedValues() => Assert.True(CorrelationIdentifierFactory.IsValid(CorrelationIdentifierFactory.Create(new string('a', 65)).Value));
}
