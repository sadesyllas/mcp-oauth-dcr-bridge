using McpOAuthDcrBridge.OAuth;
using McpOAuthDcrBridge.TestSupport;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Fuzzing;

/// <summary>
/// Deterministic fuzz coverage for the bridge's shared query/form parsing boundary
/// (<see cref="OAuthFormParameters"/> over <see cref="QueryHelpers.ParseQuery"/>), used by the
/// authorization endpoint's query string and the token endpoint's form body alike.
/// </summary>
public sealed class QueryAndFormParsingFuzzTests
{
    private const int Iterations = 2000;
    private static readonly HashSet<string> InspectedNames = new(StringComparer.Ordinal) { "client_id", "redirect_uri", "response_type", "code_challenge", "code_challenge_method", "scope", "state", "code", "code_verifier", "grant_type", "refresh_token" };

    [Fact]
    public void FuzzedQueryStringsNeverThrowThroughDuplicateDetectionOrSingleValueLookup()
    {
        var fuzzer = new DeterministicFuzzer(seed: 4);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var query = FuzzedFormEncodedText(fuzzer);
            var exception = Record.Exception(() =>
            {
                var parsed = QueryHelpers.ParseQuery(query);
                OAuthFormParameters.HasDuplicate(parsed, InspectedNames);
                foreach (var name in InspectedNames)
                {
                    OAuthFormParameters.TrySingleValue(parsed, name, out _);
                }
            });
            Assert.Null(exception);
        }
    }

    [Fact]
    public void TrySingleValueNeverReturnsTrueForAnAmbiguousOrEmptyValue()
    {
        var fuzzer = new DeterministicFuzzer(seed: 5);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var parsed = QueryHelpers.ParseQuery(FuzzedFormEncodedText(fuzzer));
            foreach (var name in InspectedNames)
            {
                if (OAuthFormParameters.TrySingleValue(parsed, name, out var value))
                {
                    Assert.Equal(1, parsed[name].Count);
                    Assert.NotNull(value);
                }
            }
        }
    }

    /// <summary>Builds fuzzed <c>application/x-www-form-urlencoded</c>-shaped text, mixing well-formed pairs with raw structural noise.</summary>
    private static string FuzzedFormEncodedText(DeterministicFuzzer fuzzer)
    {
        var pairCount = fuzzer.NextInt(6);
        var pairs = new List<string>(pairCount);
        for (var index = 0; index < pairCount; index++)
        {
            var name = fuzzer.NextInt(2) == 0 ? "client_id" : fuzzer.NextText(10);
            pairs.Add($"{name}={fuzzer.NextText(15)}");
        }

        return string.Join(fuzzer.NextInt(2) == 0 ? "&" : fuzzer.NextText(3), pairs);
    }
}
