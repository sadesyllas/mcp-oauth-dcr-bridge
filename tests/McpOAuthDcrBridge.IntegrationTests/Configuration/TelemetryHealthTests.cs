using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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

    [Fact]
    public async Task OtlpExporterIsDisabledWhenNoCollectorEndpointIsConfigured()
    {
        await using var collector = new LocalOtlpCollector(HttpStatusCode.OK);
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Arguments);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await application.StopAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Empty(collector.RequestPaths);
    }

    [Fact]
    public async Task ConfiguredLocalOtlpCollectorReceivesTraceAndMetricExportsWithoutChangingHealth()
    {
        await using var collector = new LocalOtlpCollector(HttpStatusCode.OK);
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(OtlpArguments(collector.Endpoint));
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        application.Services.GetRequiredService<TracerProvider>().ForceFlush();
        application.Services.GetRequiredService<MeterProvider>().ForceFlush();
        await application.StopAsync();
        await collector.WaitForRequestCountAsync(2);
        Assert.All(collector.RequestPaths, path => Assert.Equal("/", path));
    }

    [Fact]
    public async Task FailingLocalOtlpCollectorCannotChangeApplicationResponsesOrExposeCanaries()
    {
        const string canary = "otlp-failure-canary-406b";
        await using var collector = new LocalOtlpCollector(HttpStatusCode.InternalServerError);
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(OtlpArguments(collector.Endpoint));
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var response = await client.GetAsync($"/health/ready?input={canary}");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(canary, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await application.StopAsync();
        await collector.WaitForRequestCountAsync(1);
        Assert.All(collector.RequestBodies, body => Assert.DoesNotContain(canary, body, StringComparison.Ordinal));
    }

    private static string[] OtlpArguments(Uri endpoint) => ValidBridgeCommandLine.Arguments.Concat([
        "--environment", "Development",
        "--Bridge:AllowHttpForLocalDevelopment", "true",
        "--Bridge:Telemetry:OtlpEndpoint", endpoint.AbsoluteUri,
    ]).ToArray();

    private sealed class LocalOtlpCollector : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task receiveLoop;
        private readonly HttpStatusCode responseStatus;

        public LocalOtlpCollector(HttpStatusCode responseStatus)
        {
            this.responseStatus = responseStatus;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Endpoint = new Uri($"http://127.0.0.1:{port}/");
            receiveLoop = ReceiveAsync();
        }

        public Uri Endpoint { get; }
        public ConcurrentQueue<string> RequestPaths { get; } = new();
        public ConcurrentQueue<string> RequestBodies { get; } = new();

        public async Task WaitForRequestCountAsync(int count)
        {
            var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (RequestPaths.Count < count && DateTimeOffset.UtcNow < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }

            Assert.True(RequestPaths.Count >= count, $"Expected {count} OTLP export requests but observed {RequestPaths.Count}.");
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try { await receiveLoop; }
            catch (SocketException) when (cancellation.IsCancellationRequested) { }
        }

        private async Task ReceiveAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, leaveOpen: true))
                using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    var requestLine = await reader.ReadLineAsync(cancellation.Token) ?? string.Empty;
                    RequestPaths.Enqueue(requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? string.Empty);
                    string? header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(cancellation.Token)))
                    {
                    }

                    RequestBodies.Enqueue(string.Empty);
                    await writer.WriteAsync($"HTTP/1.1 {(int)responseStatus} {responseStatus}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await writer.FlushAsync(cancellation.Token);
                }
            }
        }
    }
}
