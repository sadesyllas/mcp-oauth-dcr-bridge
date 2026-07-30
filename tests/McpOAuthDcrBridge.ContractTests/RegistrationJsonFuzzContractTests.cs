using System.Net;
using System.Text;
using McpOAuthDcrBridge.TestSupport;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

/// <summary>
/// Deterministic fuzz coverage for the registration endpoint's JSON parsing boundary: whatever bytes
/// arrive as an <c>application/json</c> body, the endpoint must always fail closed with a bounded
/// status code and never throw an unhandled exception into a 500.
/// </summary>
public sealed class RegistrationJsonFuzzContractTests
{
    private const int Iterations = 300;

    [Fact]
    public async Task FuzzedJsonBodiesNeverProduceAnUnhandledServerError()
    {
        await using var application = BridgeContractHost.Create(permitLimit: Iterations + 1);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var fuzzer = new DeterministicFuzzer(seed: 9);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var body = FuzzedJsonLikeBytes(fuzzer);
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync("/register", content);

            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest, $"Unexpected status {response.StatusCode} for body {Encoding.UTF8.GetString(body)}");
        }
        await application.StopAsync();
    }

    /// <summary>Builds a fuzzed payload that is sometimes JSON-shaped (braces, quotes, colons) and sometimes raw structural or binary noise.</summary>
    private static byte[] FuzzedJsonLikeBytes(DeterministicFuzzer fuzzer)
    {
        if (fuzzer.NextInt(2) == 0)
        {
            return fuzzer.NextBytes(256);
        }

        var text = $"{{\"redirect_uris\":[{fuzzer.NextText(30)}],\"{fuzzer.NextText(15)}\":\"{fuzzer.NextText(30)}\"}}";
        return Encoding.UTF8.GetBytes(text);
    }
}
