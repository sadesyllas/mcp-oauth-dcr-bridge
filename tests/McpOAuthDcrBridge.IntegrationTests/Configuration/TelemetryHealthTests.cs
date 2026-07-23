using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed class TelemetryHealthTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsAreLocallyReadyAndReturnCorrelation(string path)
    {
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Arguments);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Correlation-ID", "safe-test-correlation");
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("safe-test-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
        await application.StopAsync();
    }
}
