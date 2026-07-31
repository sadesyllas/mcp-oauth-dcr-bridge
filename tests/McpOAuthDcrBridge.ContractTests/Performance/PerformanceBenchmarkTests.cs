using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace McpOAuthDcrBridge.ContractTests.Performance;

/// <summary>
/// Repeatable performance benchmarks proving the SPEC.md §7 targets: non-streaming p95 bridge
/// processing latency under 100 concurrent requests, at least 100 requests/second on OAuth/metadata
/// endpoints, and at least 100 concurrent active MCP streams with bounded memory growth. Assertions
/// use generous regression-guard thresholds rather than the tight SPEC targets, since this suite runs
/// on whatever machine invokes it; see docs/testing.md for the actual numbers measured on the
/// documented reference environment and the methodology these tests implement.
/// </summary>
public sealed class PerformanceBenchmarkTests(ITestOutputHelper output)
{
    private const string Redirect = "https://client.example.test/callback";

    [Fact]
    public async Task NonStreamingRequestProcessingLatencyStaysBoundedUnder100ConcurrentRequests()
    {
        await using var application = BridgeContractHost.Create(permitLimit: 10_000);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var durations = new RequestDurationCapture("registration");

        // Warm up JIT/connection pool before measuring.
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Register(client)));
        durations.Clear();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Register(client)));

        var samples = durations.Samples;
        Assert.True(samples.Count >= 100, $"expected at least 100 measured requests, observed {samples.Count}");
        var p95 = Percentile(samples, 0.95);
        output.WriteLine($"Non-streaming p95 bridge processing latency over {samples.Count} requests at 100 concurrency: {p95:F3} ms");
        Assert.True(p95 < 200, $"p95 bridge processing latency regressed to {p95:F3} ms");
        await application.StopAsync();
    }

    [Fact]
    public async Task OAuthAndMetadataEndpointsSustainAtLeast100RequestsPerSecond()
    {
        await using var application = BridgeContractHost.Create(permitLimit: 10_000);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("/.well-known/oauth-authorization-server")));

        const int requestCount = 500;
        var stopwatch = Stopwatch.StartNew();
        var responses = await Task.WhenAll(Enumerable.Range(0, requestCount).Select(_ => client.GetAsync("/.well-known/oauth-authorization-server")));
        stopwatch.Stop();

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var requestsPerSecond = requestCount / stopwatch.Elapsed.TotalSeconds;
        output.WriteLine($"Sustained {requestsPerSecond:F0} requests/second for {requestCount} discovery requests in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(requestsPerSecond >= 100, $"throughput regressed to {requestsPerSecond:F0} requests/second");
        await application.StopAsync();
    }

    [Fact]
    public async Task AtLeast100ConcurrentMcpStreamsHoldBoundedMemoryGrowth()
    {
        const int concurrentStreams = 120;
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeUpstream.OnRequest = async context =>
        {
            await context.Response.WriteAsync("open");
            await context.Response.Body.FlushAsync();
            await release.Task;
            await context.Response.WriteAsync(":close");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, permitLimit: concurrentStreams * 2);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var memoryBeforeBytes = GC.GetTotalMemory(forceFullCollection: true);

        var connectTasks = Enumerable.Range(0, concurrentStreams).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", $"soak-session-{index}");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();
            return (response, stream, reader: new StreamReader(stream, Encoding.ASCII));
        }).ToArray();
        var connections = await Task.WhenAll(connectTasks);
        Assert.All(connections, connection => Assert.Equal(HttpStatusCode.OK, connection.response.StatusCode));

        var memoryWhileActiveBytes = GC.GetTotalMemory(forceFullCollection: false);

        release.SetResult();
        var bodies = await Task.WhenAll(connections.Select(connection => connection.reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30))));
        Assert.All(bodies, body => Assert.Equal("open:close", body));
        foreach (var connection in connections)
        {
            connection.reader.Dispose();
            connection.response.Dispose();
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var memoryAfterBytes = GC.GetTotalMemory(forceFullCollection: true);

        var growthWhileActiveMib = (memoryWhileActiveBytes - memoryBeforeBytes) / (1024.0 * 1024.0);
        var residualGrowthMib = (memoryAfterBytes - memoryBeforeBytes) / (1024.0 * 1024.0);
        output.WriteLine($"Managed heap growth with {concurrentStreams} concurrent streams active: {growthWhileActiveMib:F2} MiB; residual after close: {residualGrowthMib:F2} MiB");
        Assert.True(growthWhileActiveMib < 200, $"managed heap growth while {concurrentStreams} streams were active regressed to {growthWhileActiveMib:F2} MiB");
        await application.StopAsync();
    }

    private static Task<HttpResponseMessage> Register(HttpClient client) =>
        client.PostAsJsonAsync("/register", new { redirect_uris = new[] { Redirect } });

    private static double Percentile(List<double> sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedSamples.Count) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Count - 1)];
    }

    /// <summary>Captures <c>bridge.request.duration</c> measurements for a bounded route via the real OpenTelemetry meter, so latency reflects the bridge's own processing time rather than client-observed round trip time.</summary>
    private sealed class RequestDurationCapture : IDisposable
    {
        private readonly string route;
        private readonly MeterListener listener;
        private readonly List<double> samples = [];
        private readonly Lock gate = new();

        public RequestDurationCapture(string route)
        {
            this.route = route;
            listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "McpOAuthDcrBridge" && instrument.Name == "bridge.request.duration")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>(OnMeasurement);
            listener.Start();
        }

        public List<double> Samples
        {
            get
            {
                lock (gate)
                {
                    var sorted = new List<double>(samples);
                    sorted.Sort();
                    return sorted;
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                samples.Clear();
            }
        }

        private void OnMeasurement(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "route" && (string?)tag.Value == route)
                {
                    lock (gate)
                    {
                        samples.Add(measurement);
                    }

                    return;
                }
            }
        }

        public void Dispose() => listener.Dispose();
    }
}
