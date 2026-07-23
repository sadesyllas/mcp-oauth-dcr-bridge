using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using McpOAuthDcrBridge;
using McpOAuthDcrBridge.IntegrationTests.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests;

public sealed class HostLifecycleTests
{
    [Fact]
    public async Task HostStartsWithoutProductEndpointsAndStopsGracefully()
    {
        using var cancellation = new CancellationTokenSource();
        await using var application = BridgeApplication.Build(ValidBridgeCommandLine.Arguments);
        var applicationLifetime = application.Services.GetRequiredService<IHostApplicationLifetime>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedRegistration = applicationLifetime.ApplicationStarted.Register(started.SetResult);
        using var stoppedRegistration = applicationLifetime.ApplicationStopped.Register(stopped.SetResult);
        var runTask = application.RunAsync(cancellation.Token);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

            var response = await client.GetAsync("/");

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

            cancellation.Cancel();

            await Task.WhenAll(runTask, stopped.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
        }
    }
}
