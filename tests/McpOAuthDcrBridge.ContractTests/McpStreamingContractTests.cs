using System.Diagnostics;
using System.Net;
using System.Text;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class McpStreamingContractTests
{
    [Fact]
    public async Task IncrementalSseDeliveryReachesTheClientBeforeTheUpstreamResponseCompletes()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var releaseSecondEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeUpstream.OnRequest = async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync("data: first\n\n");
            await context.Response.Body.FlushAsync();
            await releaseSecondEvent.Task;
            await context.Response.WriteAsync("data: second\n\n");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        var firstEventBuffer = new byte[13];

        // The upstream handler is still blocked on releaseSecondEvent; if the proxy buffered the
        // whole response first, this bounded read would hang until the handler finished.
        await stream.ReadExactlyAsync(firstEventBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("data: first\n\n", Encoding.ASCII.GetString(firstEventBuffer));
        releaseSecondEvent.TrySetResult();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        Assert.Equal("data: second\n\n", await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await application.StopAsync();
    }

    [Fact]
    public async Task LargeStreamedResponseIsDeliveredIncrementallyRatherThanBufferedInFull()
    {
        const int chunkCount = 200;
        const int chunkSize = 64 * 1024;
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var releaseRemainder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeUpstream.OnRequest = async context =>
        {
            await context.Response.WriteAsync("first-chunk");
            await context.Response.Body.FlushAsync();
            await releaseRemainder.Task;
            var chunk = new byte[chunkSize];
            Array.Fill(chunk, (byte)'x');
            for (var i = 0; i < chunkCount; i++)
            {
                await context.Response.Body.WriteAsync(chunk);
            }
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        var firstChunkBuffer = new byte[11];

        // The upstream has not written any of the ~12.5 MiB remainder yet; if the proxy buffered the
        // whole response before relaying it, this bounded read would hang until the remainder was
        // written instead of completing immediately.
        await stream.ReadExactlyAsync(firstChunkBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("first-chunk", Encoding.ASCII.GetString(firstChunkBuffer));

        releaseRemainder.TrySetResult();
        var received = 0L;
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(15))) > 0)
        {
            received += read;
        }

        Assert.Equal((long)chunkCount * chunkSize, received);
        await application.StopAsync();
    }

    [Fact]
    public async Task SessionAndReconnectHeadersSurviveAcrossSeparateRequests()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = context =>
        {
            context.Response.Headers["Mcp-Session-Id"] = "session-reconnect-abc";
            context.Response.Headers["X-Echo-Session"] = context.Request.Headers["Mcp-Session-Id"].ToString();
            context.Response.Headers["X-Echo-Last-Event-Id"] = context.Request.Headers["Last-Event-ID"].ToString();
            context.Response.Headers["X-Echo-Accept"] = context.Request.Headers.Accept.ToString();
            return context.Response.WriteAsync("ok");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        initialRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        initialRequest.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        using var initialResponse = await client.SendAsync(initialRequest);
        var sessionId = initialResponse.Headers.GetValues("Mcp-Session-Id").Single();

        // A separate connection/request simulates a client reconnecting with the session and
        // last-event-id it was given, per Streamable HTTP resumption semantics.
        using var reconnectRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        reconnectRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        reconnectRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        reconnectRequest.Headers.TryAddWithoutValidation("Last-Event-ID", "42");
        reconnectRequest.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        using var reconnectResponse = await client.SendAsync(reconnectRequest);

        Assert.Equal("session-reconnect-abc", sessionId);
        Assert.Equal(sessionId, reconnectResponse.Headers.GetValues("X-Echo-Session").Single());
        Assert.Equal("42", reconnectResponse.Headers.GetValues("X-Echo-Last-Event-Id").Single());
        Assert.Equal("text/event-stream", reconnectResponse.Headers.GetValues("X-Echo-Accept").Single());
        await application.StopAsync();
    }

    [Fact]
    public async Task AtLeast100ConcurrentActiveStreamsRemainIsolatedPerSession()
    {
        const int concurrentSessions = 120;
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var arrivedCount = 0;
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeUpstream.OnRequest = async context =>
        {
            var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
            await context.Response.WriteAsync($"first:{sessionId}");
            await context.Response.Body.FlushAsync();

            // Every handler blocks here until all 120 have simultaneously reached this point, so a
            // bridge that serializes or caps concurrent MCP streams below the target deadlocks this
            // rendezvous (and fails by timeout) instead of passing on sequential throughput.
            if (Interlocked.Increment(ref arrivedCount) == concurrentSessions)
            {
                allArrived.TrySetResult();
            }

            await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await context.Response.WriteAsync($":second:{sessionId}");
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, permitLimit: concurrentSessions * 2);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        var stopwatch = Stopwatch.StartNew();
        var results = await Task.WhenAll(Enumerable.Range(0, concurrentSessions).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", $"session-{index}");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var body = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(35));
            return (Index: index, response.StatusCode, Body: body);
        }));
        stopwatch.Stop();

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        Assert.All(results, result => Assert.Equal($"first:session-{result.Index}:second:session-{result.Index}", result.Body));
        Assert.Equal(concurrentSessions, fakeUpstream.RequestCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(35), $"expected bounded completion time, took {stopwatch.Elapsed}");
        await application.StopAsync();
    }

    [Fact]
    public async Task ActiveStreamWithFrequentDataNeverTripsTheActivityTimeout()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = async context =>
        {
            for (var i = 0; i < 4; i++)
            {
                await context.Response.WriteAsync($"chunk{i}");
                await context.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(400));
            }
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:McpActivityTimeoutSeconds");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("chunk0chunk1chunk2chunk3", await response.Content.ReadAsStringAsync());
        await application.StopAsync();
    }

    [Fact]
    public async Task IdleStreamExceedingTheActivityTimeoutEndsPredictablyRatherThanHanging()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var neverRelease = new TaskCompletionSource();
        fakeUpstream.OnRequest = async context =>
        {
            await context.Response.WriteAsync("chunk");
            await context.Response.Body.FlushAsync();
            await neverRelease.Task.WaitAsync(TimeSpan.FromSeconds(30)).ContinueWith(_ => { }, TaskScheduler.Default);
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:McpActivityTimeoutSeconds");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        var firstChunk = new byte[5];
        await stream.ReadExactlyAsync(firstChunk).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("chunk", Encoding.ASCII.GetString(firstChunk));

        var stopwatch = Stopwatch.StartNew();
        using var remainder = new MemoryStream();
        try
        {
            await stream.CopyToAsync(remainder).WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (Exception)
        {
            // Either the connection is aborted by the activity timeout or the read completes; both
            // are acceptable, but it must happen near the configured 1-second timeout, not hang.
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(7), $"expected the idle stream to end near the configured activity timeout, took {stopwatch.Elapsed}");
        neverRelease.TrySetResult();
        await application.StopAsync();
    }

    [Fact]
    public async Task ClientCancellationPromptlyCancelsUpstreamIo()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var upstreamCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstChunkSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeUpstream.OnRequest = async context =>
        {
            context.RequestAborted.Register(() => upstreamCancelled.TrySetResult());
            await context.Response.WriteAsync("chunk");
            await context.Response.Body.FlushAsync();
            firstChunkSent.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await firstChunkSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stream = await response.Content.ReadAsStreamAsync();
        await stream.ReadExactlyAsync(new byte[5]).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        // The upstream handler is now blocked for 30 seconds with no further data available, so this
        // read genuinely waits on the connection instead of returning already-buffered bytes. The
        // cancellation token must be tied to that in-progress read for it to actually abort the
        // connection; cancelling a token from an already-completed SendAsync call has no effect.
        var readTask = stream.ReadAsync(new byte[16], cancellation.Token).AsTask();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);

        await upstreamCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await application.StopAsync();
    }

    [Fact]
    public async Task AbruptUpstreamDisconnectMidStreamSurfacesAsAFailureWithoutRetry()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        fakeUpstream.OnRequest = async context =>
        {
            await context.Response.WriteAsync("partial-body");
            await context.Response.Body.FlushAsync();
            context.Abort();
        };
        await using var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint);
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // The response has already started (status/headers sent for a 200), so an abrupt upstream
        // disconnect can only truncate the body, never retroactively replace the status with an
        // error page.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var body = await response.Content.ReadAsStringAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (body != "partial-body")
            {
                throw new InvalidOperationException("stream ended without signaling truncation");
            }
        });
        Assert.Equal(1, fakeUpstream.RequestCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task ProtocolInvalidUpstreamResponseMapsToABoundedGatewayErrorWithoutRetryOrLeakingRawBytes()
    {
        const string garbage = "NOT-HTTP/9.9 garbage that must never reach the downstream client\r\n\r\n";
        await using var rawUpstream = await RawTcpUpstreamServer.StartAsync(garbage);
        await using var application = BridgeContractHost.CreateWithUpstreamMcp($"{rawUpstream.BaseUrl}/api/streamable");
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");

        using var response = await client.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(15));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode is 502 or 503 or 504, $"expected a bounded gateway error, got {(int)response.StatusCode}");
        Assert.DoesNotContain("NOT-HTTP", body, StringComparison.Ordinal);
        Assert.DoesNotContain("garbage", body, StringComparison.Ordinal);
        Assert.Equal(1, rawUpstream.ConnectionCount);
        await application.StopAsync();
    }

    [Fact]
    public async Task GracefulShutdownDrainsWithinTheConfiguredBoundedWindow()
    {
        await using var fakeUpstream = await FakeUpstreamMcpServer.StartAsync();
        var neverRelease = new TaskCompletionSource();
        fakeUpstream.OnRequest = async context =>
        {
            await context.Response.WriteAsync("chunk");
            await context.Response.Body.FlushAsync();
            await neverRelease.Task;
        };
        var application = BridgeContractHost.CreateWithUpstreamMcp(fakeUpstream.McpEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Limits:ShutdownDrainTimeoutSeconds");
            arguments.Add("1");
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer canary-token");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        var firstChunk = new byte[5];
        await stream.ReadExactlyAsync(firstChunk).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        var stopwatch = Stopwatch.StartNew();
        await application.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        // The in-flight stream never completes on its own; shutdown must still return in a bounded
        // window near the configured drain timeout rather than waiting forever.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"expected shutdown to drain within a bounded window, took {stopwatch.Elapsed}");
        neverRelease.TrySetResult();
        await application.DisposeAsync();
    }
}
