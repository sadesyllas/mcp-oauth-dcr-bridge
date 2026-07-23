using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        var originalEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", collector.Endpoint.AbsoluteUri);
        try
        {
            await using var application = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Arguments);
            await application.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
            using var response = await client.GetAsync("/health/ready");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            await application.StopAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.Empty(collector.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", originalEndpoint);
        }
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
        await collector.WaitForRequestCountAsync(1);
        application.Services.GetRequiredService<MeterProvider>().ForceFlush();
        await collector.WaitForRequestCountAsync(2);
        await application.StopAsync();
        Assert.All(collector.Requests, request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.Contains("application/x-protobuf", request.Headers["Content-Type"], StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(request.Body);
        });
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
        application.Services.GetRequiredService<TracerProvider>().ForceFlush();
        application.Services.GetRequiredService<MeterProvider>().ForceFlush();
        await application.StopAsync();
        await collector.WaitForRequestCountAsync(1);
        Assert.All(collector.Requests, request => Assert.DoesNotContain(canary, $"{string.Join(';', request.Headers.Select(header => $"{header.Key}={header.Value}"))}\n{request.Body}", StringComparison.Ordinal));
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
        internal ConcurrentQueue<CapturedOtlpRequest> Requests { get; } = new();

        public async Task WaitForRequestCountAsync(int count)
        {
            var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (Requests.Count < count && DateTimeOffset.UtcNow < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }

            Assert.True(Requests.Count >= count, $"Expected {count} OTLP export requests but observed {Requests.Count}.");
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
                    var requestLineParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(cancellation.Token)))
                    {
                        var separator = header.IndexOf(':', StringComparison.Ordinal);
                        if (separator > 0)
                        {
                            headers[header[..separator]] = header[(separator + 1)..].Trim();
                        }
                    }

                    var body = headers.ContainsKey("Transfer-Encoding")
                        ? await ReadChunkedBodyAsync(reader)
                        : await ReadDeclaredBodyAsync(reader, headers);

                    Requests.Enqueue(new CapturedOtlpRequest(requestLineParts.ElementAtOrDefault(0) ?? string.Empty, requestLineParts.ElementAtOrDefault(1) ?? string.Empty, headers, body));
                    await writer.WriteAsync($"HTTP/1.1 {(int)responseStatus} {responseStatus}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await writer.FlushAsync(cancellation.Token);
                }
            }
        }

        private static async Task<string> ReadDeclaredBodyAsync(StreamReader reader, Dictionary<string, string> headers)
        {
            var contentLength = headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var parsedLength) ? parsedLength : 0;
            return await ReadCharactersAsync(reader, contentLength);
        }

        private static async Task<string> ReadChunkedBodyAsync(StreamReader reader)
        {
            var body = new StringBuilder();
            while (true)
            {
                var lengthLine = await reader.ReadLineAsync();
                var lengthText = lengthLine?.Split(';', 2)[0] ?? "0";
                var length = Convert.ToInt32(lengthText, 16);
                if (length == 0)
                {
                    await reader.ReadLineAsync();
                    return body.ToString();
                }

                body.Append(await ReadCharactersAsync(reader, length));
                await reader.ReadLineAsync();
            }
        }

        private static async Task<string> ReadCharactersAsync(StreamReader reader, int length)
        {
            var buffer = new char[length];
            var read = await reader.ReadAsync(buffer.AsMemory());
            return new string(buffer, 0, read);
        }

        internal sealed record CapturedOtlpRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);
    }
}
