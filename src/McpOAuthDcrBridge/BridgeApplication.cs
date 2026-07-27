using McpOAuthDcrBridge.Authorization;
using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.Discovery;
using McpOAuthDcrBridge.Mcp;
using McpOAuthDcrBridge.Registration;
using McpOAuthDcrBridge.Token;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;
using McpOAuthDcrBridge.Telemetry;

namespace McpOAuthDcrBridge;

/// <summary>
/// Creates the bridge application host from its command-line configuration.
/// </summary>
public static class BridgeApplication
{
    private static readonly string[] RateLimitedEndpointPolicies = ["dcr", "authorize", "token"];

    /// <summary>
    /// Builds the endpoint-free application host used by the executable and lifecycle tests.
    /// </summary>
    /// <param name="args">The command-line configuration arguments for the host.</param>
    /// <returns>A configured application host that has not yet been started.</returns>
    public static WebApplication Build(string[] args)
        => Build(args, null, null);

    /// <summary>
    /// Builds the endpoint-free application host with an optional additional configuration source.
    /// </summary>
    /// <param name="args">The command-line configuration arguments for the host.</param>
    /// <param name="additionalConfiguration">An optional configuration source with precedence over command-line values.</param>
    /// <returns>A configured application host that has not yet been started.</returns>
    public static WebApplication Build(string[] args, IConfiguration? additionalConfiguration)
        => Build(args, additionalConfiguration, null);

    /// <summary>
    /// Builds the endpoint-free application host with optional testable configuration and logging additions.
    /// </summary>
    /// <param name="args">The command-line configuration arguments for the host.</param>
    /// <param name="additionalConfiguration">An optional configuration source with precedence over command-line values.</param>
    /// <param name="configureLogging">An optional callback that adds logging sinks after the bridge's safe policy is installed.</param>
    /// <returns>A configured application host that has not yet been started.</returns>
    public static WebApplication Build(string[] args, IConfiguration? additionalConfiguration, Action<ILoggingBuilder>? configureLogging)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (additionalConfiguration is not null)
        {
            builder.Configuration.AddConfiguration(additionalConfiguration);
        }
        var bridgeOptions = BridgeOptionsFactory.Create(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Logging.ConfigureBridgeLogging(builder.Environment.IsDevelopment());
        configureLogging?.Invoke(builder.Logging);
        builder.Services.AddSingleton(bridgeOptions);
        builder.Services.AddBridgeTelemetry(bridgeOptions, builder.Environment.IsDevelopment());
        builder.Services.AddHealthChecks();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            foreach (var policyName in RateLimitedEndpointPolicies)
            {
                options.AddFixedWindowLimiter(policyName, limiter =>
                {
                    limiter.PermitLimit = bridgeOptions.Limits.RateLimitPermitLimit;
                    limiter.Window = bridgeOptions.Limits.RateLimitWindow;
                    limiter.QueueLimit = 0;
                });
            }
        });
        builder.Services.AddHttpClient(TokenEndpointExtensions.HttpClientName, client => client.Timeout = bridgeOptions.Limits.OAuthTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.Services.AddMcpReverseProxy(bridgeOptions);

        var application = builder.Build();
        application.UseBridgeTelemetry();
        application.UseExceptionHandler(errorApplication => errorApplication.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            if (context.Items[typeof(CorrelationIdentifier)] is CorrelationIdentifier correlation)
            {
                context.Response.Headers[CorrelationIdentifier.HeaderName] = correlation.Value;
            }
            return Task.CompletedTask;
        }));
        application.UseRateLimiter();
        application.UseMiddleware<McpChallengeMiddleware>();
        application.MapHealthChecks("/health/live");
        application.MapHealthChecks("/health/ready");
        application.MapDiscoveryEndpoints(bridgeOptions);
        application.MapRegistrationEndpoint(bridgeOptions);
        application.MapAuthorizationEndpoint(bridgeOptions);
        application.MapTokenEndpoint(bridgeOptions);
        application.MapReverseProxy();

        return application;
    }
}
