using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests;

public sealed class HostLifecycleTests
{
    [Fact]
    public async Task HostStartsWithoutProductEndpointsAndStopsGracefully()
    {
        var factory = new WebApplicationFactory<Program>();

        try
        {
            var applicationLifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stoppingRegistration = applicationLifetime.ApplicationStopping.Register(stopped.SetResult);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            factory.Dispose();

            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            factory.Dispose();
        }
    }
}
