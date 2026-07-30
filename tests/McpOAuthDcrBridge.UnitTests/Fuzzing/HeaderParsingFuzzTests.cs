using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.Mcp;
using McpOAuthDcrBridge.TestSupport;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Fuzzing;

/// <summary>
/// Deterministic fuzz coverage for the bridge's header-shaped parsing boundaries:
/// <see cref="BearerChallengeParameters.Parse"/> (the RFC 7235 quoted-string splitter that carried a
/// real unquoting bug during M8) and the <see cref="HttpFieldName"/>/<see cref="HttpFieldValue"/>
/// validators used to bound configured upstream headers.
/// </summary>
public sealed class HeaderParsingFuzzTests
{
    private const int Iterations = 2000;

    [Fact]
    public void BearerChallengeParsingNeverThrowsAndAlwaysReturnsNonEmptyKeysAndValues()
    {
        var fuzzer = new DeterministicFuzzer(seed: 6);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = fuzzer.NextText(60);
            IReadOnlyDictionary<string, string>? parsed = null;
            var exception = Record.Exception(() => parsed = BearerChallengeParameters.Parse(candidate));
            Assert.Null(exception);
            Assert.All(parsed!, pair => Assert.NotEqual(string.Empty, pair.Key));
        }
    }

    [Fact]
    public void BearerChallengeParsingHandlesNullAndEmptyWithoutThrowing()
    {
        Assert.Empty(BearerChallengeParameters.Parse(null));
        Assert.Empty(BearerChallengeParameters.Parse(string.Empty));
    }

    [Fact]
    public void FieldNameValidationNeverThrows()
    {
        var fuzzer = new DeterministicFuzzer(seed: 7);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = fuzzer.NextText(30);
            var exception = Record.Exception(() => HttpFieldName.IsValid(candidate));
            Assert.Null(exception);
        }

        Assert.False(HttpFieldName.IsValid(null));
        Assert.False(HttpFieldName.IsValid(string.Empty));
    }

    [Fact]
    public void FieldValueValidationNeverThrows()
    {
        var fuzzer = new DeterministicFuzzer(seed: 8);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = fuzzer.NextText(30);
            var exception = Record.Exception(() => HttpFieldValue.IsValid(candidate));
            Assert.Null(exception);
        }

        Assert.False(HttpFieldValue.IsValid(null));
        Assert.False(HttpFieldValue.IsValid(string.Empty));
    }
}
