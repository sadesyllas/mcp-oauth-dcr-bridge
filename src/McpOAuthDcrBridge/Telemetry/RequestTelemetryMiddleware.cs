using System.Diagnostics;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Records bounded request diagnostics without inspecting credentials, query strings, or bodies.
/// </summary>
public sealed partial class RequestTelemetryMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<RequestTelemetryMiddleware> logger;

    /// <summary>Initializes a new safe request telemetry middleware instance.</summary>
    /// <param name="next">The next request handler.</param>
    /// <param name="logger">The structured logger used for safe completion events.</param>
    public RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    /// <summary>Records request telemetry around the remaining pipeline.</summary>
    /// <param name="context">The inbound HTTP context.</param>
    /// <returns>A task that completes after completion telemetry is emitted.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var route = NormalizedRoute(context.Request.Path);
        var correlation = (CorrelationIdentifier?)context.Items[typeof(CorrelationIdentifier)];
        using var activity = BridgeTelemetry.ActivitySource.StartActivity("bridge.request");
        activity?.SetTag("bridge.route", route);
        activity?.SetTag("bridge.method", context.Request.Method);
        activity?.SetTag("bridge.correlation_id", correlation?.Value);

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusClass = $"{context.Response.StatusCode / 100}xx";
            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
            BridgeTelemetry.RequestCount.Add(1, new KeyValuePair<string, object?>("route", route), new KeyValuePair<string, object?>("status", statusClass));
            BridgeTelemetry.RequestDurationMilliseconds.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("route", route));
            LogRequestCompleted(logger, route, statusClass, stopwatch.Elapsed.TotalMilliseconds, correlation?.Value);
        }
    }

    private static string NormalizedRoute(PathString path) => path.Value switch
    {
        "/health/live" => "/health/live",
        "/health/ready" => "/health/ready",
        _ => "other",
    };

    [LoggerMessage(LogLevel.Information, "Bridge request completed for {Route} with {StatusClass} in {ElapsedMilliseconds} ms, correlation {CorrelationId}")]
    private static partial void LogRequestCompleted(ILogger logger, string route, string statusClass, double elapsedMilliseconds, string? correlationId);
}
