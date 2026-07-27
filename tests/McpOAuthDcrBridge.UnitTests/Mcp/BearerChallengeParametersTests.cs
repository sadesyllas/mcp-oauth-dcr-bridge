using McpOAuthDcrBridge.Mcp;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Mcp;

public sealed class BearerChallengeParametersTests
{
    [Fact]
    public void ParseReturnsEmptyForNullOrEmptyInput()
    {
        Assert.Empty(BearerChallengeParameters.Parse(null));
        Assert.Empty(BearerChallengeParameters.Parse(string.Empty));
    }

    [Fact]
    public void ParseExtractsQuotedNameValuePairs()
    {
        var parameters = BearerChallengeParameters.Parse("realm=\"mcp\", error=\"insufficient_scope\", scope=\"mcp.read mcp.write\"");

        Assert.Equal("mcp", parameters["realm"]);
        Assert.Equal("insufficient_scope", parameters["error"]);
        Assert.Equal("mcp.read mcp.write", parameters["scope"]);
    }

    [Fact]
    public void ParseIsCaseInsensitiveForParameterNames()
    {
        var parameters = BearerChallengeParameters.Parse("Error=\"invalid_token\"");

        Assert.Equal("invalid_token", parameters["error"]);
        Assert.Equal("invalid_token", parameters["ERROR"]);
    }

    [Fact]
    public void ParseTrimsSurroundingWhitespaceAroundNamesAndValues()
    {
        var parameters = BearerChallengeParameters.Parse("  error = \"invalid_token\"  ,  scope=\"a\"  ");

        Assert.Equal("invalid_token", parameters["error"]);
        Assert.Equal("a", parameters["scope"]);
    }

    [Fact]
    public void ParseIgnoresMalformedPairsWithoutThrowing()
    {
        var parameters = BearerChallengeParameters.Parse("justtext, =novalue, error=\"invalid_token\"");

        Assert.Single(parameters);
        Assert.Equal("invalid_token", parameters["error"]);
    }

    [Fact]
    public void ParseHandlesAnUnquotedValue()
    {
        var parameters = BearerChallengeParameters.Parse("error=invalid_token");

        Assert.Equal("invalid_token", parameters["error"]);
    }
}
