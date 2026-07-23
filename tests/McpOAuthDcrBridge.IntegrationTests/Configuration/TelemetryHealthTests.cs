using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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

    [Fact]
    public async Task ExceptionBoundaryReturnsBoundedFailureWithCorrelation()
    {
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Arguments);
        application.MapGet("/test-throw", (HttpContext _) => throw new InvalidOperationException("telemetry-canary-secret"));
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/test-throw");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.DoesNotContain("telemetry-canary-secret", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await application.StopAsync();
    }

    [Fact]
    public async Task OptionalOtlpExporterDoesNotChangeLocalHealthBehavior()
    {
        var arguments = ValidBridgeCommandLine.Arguments.Concat(["--Bridge:Telemetry:OtlpEndpoint", "https://127.0.0.1:1/v1/otlp"]).ToArray();
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(arguments);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await application.StopAsync();
    }
}
