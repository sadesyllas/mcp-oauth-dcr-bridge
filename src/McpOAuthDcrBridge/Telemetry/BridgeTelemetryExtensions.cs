using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Adds safe bridge telemetry services and request processing to an application host.
/// </summary>
public static class BridgeTelemetryExtensions
{
    /// <summary>Adds JSON logging, bridge-owned OpenTelemetry instruments, and optional OTLP exporters.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="options">The validated bridge configuration.</param>
    /// <param name="isDevelopment">Whether concise development logging should be used.</param>
    /// <returns>The same service collection for composition.</returns>
    public static IServiceCollection AddBridgeTelemetry(this IServiceCollection services, Configuration.BridgeOptions options, bool isDevelopment)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(BridgeTelemetry.ActivitySource.Name);
                ConfigureOtlp(tracing, options);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(BridgeTelemetry.Meter.Name);
                metrics.AddRuntimeInstrumentation();
                ConfigureOtlp(metrics, options);
            });
        return services;
    }

    /// <summary>Adds correlation, safe request telemetry, and response correlation headers.</summary>
    /// <param name="application">The application pipeline.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication UseBridgeTelemetry(this WebApplication application)
    {
        application.UseMiddleware<CorrelationMiddleware>();
        application.UseMiddleware<RequestTelemetryMiddleware>();
        return application;
    }

    private static void ConfigureOtlp(TracerProviderBuilder tracing, Configuration.BridgeOptions options)
    {
        if (options.OtlpEndpoint is not null)
        {
            tracing.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = options.OtlpEndpoint;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    }

    private static void ConfigureOtlp(MeterProviderBuilder metrics, Configuration.BridgeOptions options)
    {
        if (options.OtlpEndpoint is not null)
        {
            metrics.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = options.OtlpEndpoint;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    }
}
