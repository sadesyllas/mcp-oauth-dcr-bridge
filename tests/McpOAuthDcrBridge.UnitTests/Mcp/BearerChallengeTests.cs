using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.Mcp;
using McpOAuthDcrBridge.UnitTests.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Mcp;

public sealed class BearerChallengeTests
{
    private static readonly BridgeOptions Options = BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(), false);

    [Fact]
    public void BuildEscapesEmbeddedQuotesInPreservedValues()
    {
        var challenge = BearerChallenge.Build(Options, new Dictionary<string, string> { ["error_description"] = "say \"hi\" please" });

        Assert.Contains("error_description=\"say \\\"hi\\\" please\"", challenge, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEscapesEmbeddedBackslashesInPreservedValues()
    {
        var challenge = BearerChallenge.Build(Options, new Dictionary<string, string> { ["error_description"] = "path C:\\temp" });

        Assert.Contains("error_description=\"path C:\\\\temp\"", challenge, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("code expired, retry later")]
    [InlineData("say \"hi\" please")]
    [InlineData("backslash \\ and \"quote\"")]
    [InlineData("plain value")]
    public void BuildAndParseRoundTripPreservedValuesExactly(string value)
    {
        var original = new Dictionary<string, string> { ["error"] = "invalid_token", ["error_description"] = value, ["scope"] = "mcp.read" };

        var challenge = BearerChallenge.Build(Options, original);
        var parsed = BearerChallengeParameters.Parse(challenge["Bearer ".Length..]);

        Assert.Equal(original["error"], parsed["error"]);
        Assert.Equal(original["error_description"], parsed["error_description"]);
        Assert.Equal(original["scope"], parsed["scope"]);
    }

    [Fact]
    public void BuildWithoutPreservedParametersEmitsOnlyResourceMetadata()
    {
        var challenge = BearerChallenge.Build(Options);

        Assert.Equal($"Bearer resource_metadata=\"{Options.ProtectedResourceMetadataUri.AbsoluteUri}\"", challenge);
    }
}
