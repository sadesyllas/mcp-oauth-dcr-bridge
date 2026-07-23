using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.Discovery;
using McpOAuthDcrBridge.Registration;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using McpOAuthDcrBridge.Telemetry;

namespace McpOAuthDcrBridge;

/// <summary>
/// Creates the bridge application host from its command-line configuration.
/// </summary>
public static class BridgeApplication
{
    /// <summary>
    /// Builds the endpoint-free application host used by the executable and lifecycle tests.
    /// </summary>
    /// <param name="args">The command-line configuration arguments for the host.</param>
    /// <returns>A configured application host that has not yet been started.</returns>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var bridgeOptions = BridgeOptionsFactory.Create(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Logging.ConfigureBridgeLogging(builder.Environment.IsDevelopment());
        builder.Services.AddSingleton(bridgeOptions);
        builder.Services.AddBridgeTelemetry(bridgeOptions, builder.Environment.IsDevelopment());
        builder.Services.AddHealthChecks();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("dcr", limiter =>
            {
                limiter.PermitLimit = bridgeOptions.Limits.RateLimitPermitLimit;
                limiter.Window = bridgeOptions.Limits.RateLimitWindow;
                limiter.QueueLimit = 0;
            });
        });

        var application = builder.Build();
        application.UseBridgeTelemetry();
        application.UseRateLimiter();
        application.MapHealthChecks("/health/live");
        application.MapHealthChecks("/health/ready");
        application.MapDiscoveryEndpoints(bridgeOptions);
        application.MapRegistrationEndpoint(bridgeOptions);

        return application;
    }
}
