using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.TestSupport;
using McpOAuthDcrBridge.UnitTests.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Fuzzing;

/// <summary>
/// Deterministic fuzz coverage for the bridge's startup URI validation boundary: every value that
/// reaches <see cref="BridgeOptionsFactory.Create"/> as a configured URI must either validate or fail
/// closed with <see cref="BridgeConfigurationException"/>, never with an unhandled exception.
/// </summary>
public sealed class UriParsingFuzzTests
{
    private const int Iterations = 2000;

    [Fact]
    public void FuzzedExternalBaseUrlValuesNeverThrowUnboundedExceptions()
    {
        var fuzzer = new DeterministicFuzzer(seed: 1);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = FuzzedUriText(fuzzer);
            var exception = Record.Exception(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:ExternalBaseUrl"] = candidate), false));
            Assert.True(exception is null or BridgeConfigurationException, $"Unexpected exception type {exception?.GetType()} for input {candidate}");
        }
    }

    [Fact]
    public void FuzzedRedirectUriValuesNeverThrowUnboundedExceptions()
    {
        var fuzzer = new DeterministicFuzzer(seed: 2);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = FuzzedUriText(fuzzer);
            var exception = Record.Exception(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:AllowedRedirectUris:0"] = candidate), false));
            Assert.True(exception is null or BridgeConfigurationException, $"Unexpected exception type {exception?.GetType()} for input {candidate}");
        }
    }

    [Fact]
    public void FuzzedUpstreamEndpointValuesNeverThrowUnboundedExceptions()
    {
        var fuzzer = new DeterministicFuzzer(seed: 3);
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var candidate = FuzzedUriText(fuzzer);
            var exception = Record.Exception(() => BridgeOptionsFactory.Create(ValidBridgeConfiguration.Create(values => values["Bridge:Upstream:AuthorizationEndpoint"] = candidate), false));
            Assert.True(exception is null or BridgeConfigurationException, $"Unexpected exception type {exception?.GetType()} for input {candidate}");
        }
    }

    /// <summary>Builds a fuzzed candidate that is sometimes a well-formed HTTPS URI shape and sometimes unstructured noise, to exercise both the happy and rejection paths.</summary>
    private static string FuzzedUriText(DeterministicFuzzer fuzzer) =>
        fuzzer.NextInt(2) == 0
            ? $"https://{fuzzer.NextText(20)}/{fuzzer.NextText(20)}"
            : fuzzer.NextText(40);
}
